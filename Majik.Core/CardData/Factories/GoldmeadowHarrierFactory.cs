using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goldmeadow Harrier (Lorwyn / 10th Edition, {W}).
///
/// Creature — Kithkin Soldier 1/1. Oracle text (verified against Scryfall):
///   "{W}, {T}: Tap target creature."
///
/// The card's base shape (name, Creature type, Kithkin/Soldier subtypes,
/// {W}, 1/1) is materialised from the embedded JSON definition
/// (<c>goldmeadow-harrier.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single activated ability
/// is layered on top here — the JSON <c>AbilityDefinition</c> schema does
/// not yet express activated abilities with mana + tap costs and a target,
/// so it lives in the factory (same posture as the other JSON-backed cards
/// whose behaviour outgrows the schema, e.g. <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Kithkin Soldier at {W}.
/// - <b>{W}, {T}: Tap target creature (CR 602 + CR 701.21)</b>:
///   <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{W}"), AdditionalCost.Tap(self)]</c> + a 1..1
///   "target creature" <see cref="TargetRequest"/>. This is the same tapper
///   shape as <see cref="EndbringerFactory"/>'s third ability ({C}, {T}:
///   Tap target creature) — only the mana pip differs ({W} vs {C}).
///   Resolution re-checks the chosen target is still a creature on the
///   battlefield (CR 608.2b — illegal-on-resolution fails silently) and taps
///   via <see cref="Fx.Tap"/>. Tapping an already-tapped permanent is a
///   no-op (CR 701.21b — "taps" with no effect; <see cref="Permanent.Tap"/>
///   is idempotent).
/// </summary>
[CardName("Goldmeadow Harrier")]
public static class GoldmeadowHarrierFactory
{
    public const string CardName = "Goldmeadow Harrier";
    public const string Slug = "goldmeadow-harrier";
    public const string ActivationManaCost = "{W}";

    /// <summary>
    /// Construct Goldmeadow Harrier owned and controlled by
    /// <paramref name="owner"/>. The single tap-target-creature activated
    /// ability is attached. Resolution uses the primitive <see cref="Fx.Tap"/>
    /// helper; no <see cref="Majik.Core.Services.ZoneService"/> or
    /// <see cref="Majik.Core.Events.IEventBus"/> wiring is required for the
    /// v1 surface. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Kithkin/Soldier subtypes, {W}, 1/1). The JSON carries no abilities
        // — the activated tapper is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {W}, {T}: Tap target creature.
        // CR 602 + CR 701.21. Cost stack: ManaCostCost("{W}") +
        // AdditionalCost.Tap(self). 1..1 "target creature" TargetRequest.
        // Resolution re-checks the chosen target is still a creature on the
        // battlefield (CR 608.2b — illegal-on-resolution fails silently) and
        // taps via Fx.Tap. Tapping an already-tapped permanent is a no-op
        // (Permanent.Tap is idempotent). Same shape as Endbringer's third
        // ability, only the mana pip differs ({W} vs {C}).
        // ----------------------------------------------------------------
        ActivatedAbility? tapAbility = null;
        var tapEffect = new Effect(
            $"{CardName}: tap target creature",
            () =>
            {
                if (tapAbility == null) return;
                if (tapAbility.ChosenTargets.Count == 0) return;
                if (tapAbility.ChosenTargets[0].Count == 0) return;

                if (tapAbility.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — recheck legality at resolution.
                if (!target.HasType(CardType.Creature)) return;
                if (target.Zone != ZoneType.Battlefield) return;

                Fx.Tap(target);
            });

        tapAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { tapEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(tapAbility);

        return card;
    }
}
