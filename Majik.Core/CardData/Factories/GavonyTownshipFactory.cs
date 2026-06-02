using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gavony Township (Innistrad).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}{G}{W}, {T}: Put a +1/+1 counter on each creature you control."
///
/// ## Why it gets its own factory
/// A {C}-producing utility land whose second ability is the same
/// "+1/+1 counter on each creature you control" anthem used by
/// <see cref="SteelOverseerFactory"/> (whose printed scope is the narrower
/// "each artifact creature you control"). Gavony Township drops the artifact
/// filter and adds a {2}{G}{W} mana cost to the activation. No new engine
/// mechanic is required: the {T}: Add {C} mana ability is declared in the
/// embedded JSON, and the activated ability composes the existing
/// <see cref="ManaCostCost"/> + <see cref="AdditionalCost.Tap"/> +
/// <see cref="CountersService.Add"/> primitives.
///
/// ## Implemented (v1)
/// - <b>Land</b> identity (no printed subtypes / supertypes) from the embedded
///   JSON definition (<c>gavony-township.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>, plus owner / controller wiring.
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> (CR 605.1 — mana
///   ability, doesn't use the stack), declared in the JSON.
/// - <b>{2}{G}{W}, {T}: Put a +1/+1 counter on each creature you control.</b>
///   — an <see cref="ActivatedAbility"/> (CR 602) with two costs:
///   <see cref="ManaCostCost"/>("{2}{G}{W}") for the mana pips and
///   <see cref="AdditionalCost.Tap"/> on the land. On resolution the
///   controller's battlefield is scanned (CR 608.2 — current game state) for
///   creatures, and each receives one +1/+1 counter via
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
///   replacements observe the placement (CR 614 / CR 121.2). When no
///   <see cref="ReplacementBus"/> is supplied the counter is placed directly.
///
/// ## Rules notes
/// - "each creature you control" = the ability's controller's creatures
///   (CR 109.5). The land itself is not a creature, so it is never a target of
///   its own counters. The scan reflects the battlefield at resolution, not at
///   activation (CR 608.2).
///
/// ## Lifecycle
/// The two-arg <see cref="Create(Player, ReplacementBus?)"/> overload wires the
/// replacement bus so the activated ability honours Hardened-Scales-shaped
/// bumps. The single-arg overload returns a shape-only card (no bus) — counter
/// placements fall through to a direct add. Mirrors
/// <see cref="SteelOverseerFactory"/>'s posture.
/// </summary>
[CardName("Gavony Township")]
public static class GavonyTownshipFactory
{
    public const string CardName = "Gavony Township";
    public const string Slug = "gavony-township";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Gavony Township with no live <see cref="ReplacementBus"/>.
    /// Counter placements from the activated ability fall through to a direct
    /// add (Hardened Scales / Doubling Season etc. won't bump). Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Gavony Township with an optional <see cref="ReplacementBus"/>.
    /// The {T}: Add {C} mana ability comes from the embedded JSON; the
    /// "{2}{G}{W}, {T}: Put a +1/+1 counter on each creature you control"
    /// activated ability is layered on structurally.
    /// </summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Land, {T}: Add {C}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {2}{G}{W}, {T}: Put a +1/+1 counter on each creature you control.
        // CR 602 — activated ability with two costs (mana pips + tap). At
        // resolve time the controller's battlefield is scanned for creatures
        // (CR 608.2 — current game state); each receives one +1/+1 counter
        // via CountersService.Add so Hardened Scales / Doubling Season can
        // intercept.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: +1/+1 counter on each creature you control",
            () =>
            {
                var controller = land.Controller ?? owner;
                foreach (var creature in FindCreaturesControlled(controller))
                {
                    CountersService.Add(creature, CounterType.PlusOnePlusOne, 1, replacements);
                }
            });

        var pumpAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}{G}{W}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { pumpEffect });

        land.AddAbility(pumpAbility);

        return land;
    }

    /// <summary>
    /// Enumerate the controller's battlefield creatures. Used by the activated
    /// ability at resolve time so the set reflects the current battlefield
    /// (not the set at activation time — CR 608.2 resolves with current game
    /// state).
    /// </summary>
    private static IEnumerable<Creature> FindCreaturesControlled(Player controller) =>
        controller.Zones.Battlefield.GetCards().OfType<Creature>();
}
