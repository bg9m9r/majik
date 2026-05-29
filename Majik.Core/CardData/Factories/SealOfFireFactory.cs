using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seal of Fire (Nemesis / many reprints, {R}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Sacrifice this enchantment: It deals 2 damage to any target."
///
/// The base shape (name, Enchantment, {R}, red) is materialised from the
/// embedded JSON definition (<c>seal-of-fire.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="GrimLavamancerFactory"/>). The single activated ability is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express a sacrifice-as-cost any-target ping.
///
/// ## Implemented (v1)
///
/// - Enchantment at printed cost {R}; owner / controller wired.
///
/// - <b>Sacrifice this enchantment: It deals 2 damage to any target
///   (CR 602)</b>: <see cref="ActivatedAbility"/> with a single
///   <see cref="AdditionalCost.Sacrifice"/> on Seal of Fire itself
///   (CR 602.1b / CR 118.3g — a sacrifice payment is part of the activation
///   cost). A single any-target request is declared so the activating
///   player's agent picks a damage-receiving target at activation
///   (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes the 2 damage
///   through <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert
///   to loyalty removal (CR 306.7) — same shape as Pyrite Spellbomb /
///   Lightning Bolt. Illegal-on-resolution targets fail silently
///   (CR 608.2b) — the sacrifice still resolves because the cost was paid.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub, so the effect closure performs the move-to-graveyard itself
///   (mirrors <see cref="PyriteSpellbombFactory"/> / Aether / Nihil
///   Spellbomb). Remove the explicit move once
///   <see cref="AdditionalCost.Sacrifice"/> performs the sacrifice itself.
/// </summary>
[CardName("Seal of Fire")]
public static class SealOfFireFactory
{
    public const string CardName = "Seal of Fire";
    public const string Slug = "seal-of-fire";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 — fixed 2 damage to any target.</summary>
    public const int PingDamage = 2;

    /// <summary>
    /// Construct Seal of Fire owned and controlled by <paramref name="owner"/>.
    /// The "Sacrifice: 2 damage to any target" activated ability is attached
    /// to the card. The ability is fully self-contained — no service wiring
    /// required.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Enchantment,
        // {R}, red). The JSON carries no abilities — the ping ability is
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Enchantment)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Sacrifice this enchantment: It deals 2 damage to any target.
        // CR 602 — activated ability with a single any-target request.
        // The sacrifice cost (CR 602.1b) is declared on the ability; the
        // generic AdditionalCost.Sacrifice payment is a no-op stub, so the
        // resolve closure performs the move-to-graveyard itself (mirrors
        // Pyrite Spellbomb). The resolution reads ChosenTargets and routes
        // the 2 damage through Fx.DealDamageAny (CR 306.7 loyalty route).
        // ----------------------------------------------------------------
        ActivatedAbility? pingAbility = null;
        var pingEffect = new Effect(
            $"{CardName}: 2 damage to any target + sac self",
            () =>
            {
                if (pingAbility != null
                    && pingAbility.ChosenTargets.Count > 0
                    && pingAbility.ChosenTargets[0].Count > 0)
                {
                    var target = pingAbility.ChosenTargets[0][0];
                    Fx.DealDamageAny(target, PingDamage);
                }

                SacrificeSelf(card, owner);
            });

        pingAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
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

    /// <summary>
    /// Move <paramref name="seal"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield. Mirrors
    /// the closure used by <see cref="PyriteSpellbombFactory"/>.
    /// </summary>
    private static void SacrificeSelf(Enchantment seal, Player owner)
    {
        if (seal.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(seal);
        owner.Zones.Graveyard.AddCard(seal);
        seal.SetZone(ZoneType.Graveyard);
    }
}
