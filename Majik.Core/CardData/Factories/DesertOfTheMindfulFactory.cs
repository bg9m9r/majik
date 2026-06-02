using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Desert of the Mindful (Amonkhet "cycling Desert"
/// cycle — the mono-colour, enters-tapped cycling lands that carry the
/// printed Desert subtype).
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {U}.
///    Cycling {1}{U} ({1}{U}, Discard this card: Draw a card.)"
///
/// Type line is <c>Land — Desert</c> (the printed Desert subtype, same as
/// <see cref="AbradedBluffsFactory"/>).
///
/// This is the <see cref="OnslaughtCyclingLandFactory"/> shape with two
/// differences: (1) the printed land subtype is <b>Desert</b> rather than a
/// basic-land type, and (2) the cycling cost is <c>{1}{U}</c> rather than a
/// single coloured pip. Identity + the {U} mana ability come from the JSON
/// definition (same posture as the Refuge / Triome / Desert cycles); the
/// cycling ability is wired in code via the shared
/// <see cref="CyclingFactory.Build"/> primitive because the JSON schema does
/// not yet express cycling.
///
/// ## Implemented (v1)
/// - <b>Identity + {U} mana</b> — loaded from
///   <c>Majik.Core/CardData/Cards/desert-of-the-mindful.json</c> via
///   <see cref="CardDefinitionFactory"/>: a Land with the Desert subtype and
///   a single-colour <see cref="Majik.Core.Abilities.ManaAbility"/> producing
///   {U} (CR 605.1a — mana abilities don't use the stack).
/// - <b>Enters-tapped (CR 614.1c)</b> — unconditional "This land enters
///   tapped." Applied on the production load path by
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle
///   text (this factory builds the land without it, matching the Refuge /
///   Abraded Bluffs cycle posture — the binder owns the replacement so it
///   isn't double-registered).
/// - <b>Cycling {1}{U}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>{1}{U}</c>). When a bus is supplied the
///   cycling resolve publishes <see cref="CardCycledEvent"/> (CR 702.32d
///   "Whenever a player cycles a card") so Lightning Rift / Astral Slide /
///   Drift of Phantasms-style triggers fire.
/// </summary>
[CardName("Desert of the Mindful")]
public static class DesertOfTheMindfulFactory
{
    public const string CardName = "Desert of the Mindful";

    /// <summary>Printed cycling cost — CR 702.32. Desert of the Mindful's
    /// cycling cost is <c>{1}{U}</c>.</summary>
    public const string CyclingCost = "{1}{U}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("desert-of-the-mindful");

    /// <summary>
    /// Construct Desert of the Mindful with no live wiring. Cycling is
    /// attached for shape inspection but its resolve publishes no
    /// <see cref="CardCycledEvent"/>; enters-tapped is omitted (no
    /// <see cref="ReplacementBus"/> available — the binder layer owns it on
    /// the production path). Enters untapped on this shape-only path,
    /// matching the Refuge / Abraded Bluffs cycle posture.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Desert of the Mindful. When <paramref name="eventBus"/> is
    /// supplied the cycling resolve publishes <see cref="CardCycledEvent"/>
    /// (CR 702.32d). Enters-tapped (CR 614.1c) is applied by
    /// <see cref="Majik.Core.CardData.EntersTappedBinder"/> on the
    /// production load path, not here.
    /// </summary>
    public static Land Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + {U} mana come from the JSON definition.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Cycling {1}{U}. CR 702.32 — "{1}{U}, Discard this card: Draw a
        // card." Cycle cost is ManaCostCost("{1}{U}"); the primitive
        // appends the DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost(CyclingCost), eventBus);

        return land;
    }
}
