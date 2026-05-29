using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spikeshot Goblin (Mirrodin, {2}{R}).
///
/// Creature — Goblin Shaman 1/2. Oracle text (Scryfall, verified):
///   "{R}, {T}: This creature deals damage equal to its power to any target."
///
/// The base shape (name, Creature, Goblin/Shaman subtypes, {2}{R}, 1/2) is
/// materialised from the embedded JSON definition
/// (<c>spikeshot-goblin.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="GrimLavamancerFactory"/> / <see cref="StormscaleScionFactory"/>).
/// The single activated ability is layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express a {mana}+{T}
/// any-target ping whose amount is "equal to its power".
///
/// ## Implemented (v1)
///
/// - 1/2 Creature — Goblin Shaman at printed cost {2}{R}; owner / controller
///   wired. Both <see cref="CardSubtype.Goblin"/> and
///   <see cref="CardSubtype.Shaman"/> are stamped so Goblin-tribal (Goblin
///   Chieftain / Krenko) and Shaman-tribal anchors see it correctly.
///
/// - <b>{R}, {T}: This creature deals damage equal to its power to any
///   target (CR 602)</b>: <see cref="ActivatedAbility"/> with:
///   <list type="number">
///     <item>a <see cref="ManaCostCost"/> for the printed {R} (CR 602.1b —
///       the mana symbol in the activation cost).</item>
///     <item><see cref="AdditionalCost.Tap"/> on Spikeshot Goblin (CR 602.1b
///       — the {T} symbol; summoning-sickness / tapped-state legality is
///       handled by the cost layer, same as Grim Lavamancer / Fanatical
///       Firebrand).</item>
///   </list>
///   No sacrifice (distinct from Mogg Fanatic). A single any-target request
///   is declared so the activating player's agent picks a damage-receiving
///   target at activation (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes the damage
///   through <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert
///   to loyalty removal (CR 306.7) — same shape as Grim Lavamancer / Mogg
///   Fanatic / Lightning Bolt.
///
/// ## "Damage equal to its power" — read at resolution
///
/// CR 608.2h — the amount of damage is determined as the ability resolves,
/// using the source's power at that moment. The resolve closure therefore
/// reads <see cref="Creature.Power"/> (the live, layer-aware value — base
/// plus any +X/+0 pumps such as an equipment / anthem) rather than the
/// printed 1. If a continuous effect has raised Spikeshot's power, the
/// damage scales with it. The source is the Spikeshot Goblin permanent
/// itself ("This creature deals…").
///
/// ## Deferred (v1 gaps)
///
/// - <b>Last-known-information when Spikeshot has left the battlefield</b>:
///   the closure reads <see cref="Creature.Power"/> directly. If Spikeshot
///   somehow leaves the battlefield after activation but before resolution
///   (the ability is independent of the source — CR 112.7a), the live
///   <c>Power</c> still reflects last-known base/0 rather than a formal
///   CR 608.2h last-known-information snapshot. There is no scenario on this
///   card that detaches the ability from the source mid-resolution under v1
///   wiring, so the observable contract (damage = current power) holds.
/// </summary>
[CardName("Spikeshot Goblin")]
public static class SpikeshotGoblinFactory
{
    public const string CardName = "Spikeshot Goblin";
    public const string Slug = "spikeshot-goblin";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>CR 602 — printed activation mana cost: {R}.</summary>
    public const string ActivationManaCost = "{R}";

    /// <summary>
    /// Construct Spikeshot Goblin owned and controlled by
    /// <paramref name="owner"/>. The {R}, {T}: deal-damage-equal-to-power
    /// activated ability is attached to the card. The ability is fully
    /// self-contained — no service wiring required.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Goblin/Shaman subtypes, {2}{R}, 1/2). The JSON carries no abilities
        // — the ping ability is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {R}, {T}: This creature deals damage equal to its power to any
        // target. CR 602 — activated ability with a single any-target
        // request. The mana ({R}) + tap ({T}) costs are taken by the cost
        // layer at activation; the damage is performed in the resolve
        // closure, where the amount is the source's CURRENT power
        // (CR 608.2h — determined as the ability resolves).
        // ----------------------------------------------------------------
        ActivatedAbility? pingAbility = null;
        var pingEffect = new Effect(
            $"{CardName}: damage equal to its power to any target",
            () =>
            {
                if (pingAbility == null
                    || pingAbility.ChosenTargets.Count == 0
                    || pingAbility.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                // CR 608.2h — read the source's power at resolution time.
                var amount = card.Power;
                if (amount <= 0)
                {
                    return; // 0 (or negative-floored) power deals no damage.
                }

                var target = pingAbility.ChosenTargets[0][0];
                Fx.DealDamageAny(target, amount);
            });

        pingAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { pingEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(pingAbility);

        return card;
    }
}
