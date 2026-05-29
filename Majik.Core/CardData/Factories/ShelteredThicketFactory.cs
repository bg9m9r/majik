using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sheltered Thicket (Amonkhet). Member of the
/// Amonkhet R/G dual-type cycling-tapland cycle. Oracle text (verified
/// against Scryfall):
///
/// <code>
/// ({T}: Add {R} or {G}.)
/// This land enters tapped.
/// Cycling {2} ({2}, Discard this card: Draw a card.)
/// </code>
///
/// Type line: <c>Land — Mountain Forest</c>.
///
/// <para>
/// The Land shell — the Mountain and Forest land subtypes (CR 205.3i) and
/// the two mana abilities {R}/{G} (CR 605.1 — mana abilities don't use the
/// stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/sheltered-thicket.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="AlpineMeadowFactory"/> (the Kaldheim snow dual-tapland).
/// </para>
///
/// <para>
/// Cycling {2} (CR 702.32) is layered on top via the shared
/// <see cref="CyclingFactory.Build"/> primitive with cycle cost
/// <see cref="ManaCostCost"/>("{2}"). Cycling is not (yet) an
/// <see cref="AbilityDefinition"/> shape, so it is wired in code rather
/// than in the JSON — same split as the Onslaught cycling-land cycle
/// (<see cref="OnslaughtCyclingLandFactory"/>) and
/// <see cref="DesertCerodonFactory"/>. The primitive appends the
/// <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a), attaches the
/// <see cref="KeywordAbility"/> "Cycling" marker, and on resolve draws a
/// card then publishes <see cref="CardCycledEvent"/> (CR 702.32d) when a
/// bus is supplied.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as <see cref="AlpineMeadowFactory"/>).
/// </para>
/// </summary>
[CardName("Sheltered Thicket")]
public static class ShelteredThicketFactory
{
    /// <summary>Cycling cost — CR 702.32, printed <c>Cycling {2}</c>.</summary>
    public const string CyclingCost = "{2}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sheltered-thicket");

    /// <summary>
    /// Construct Sheltered Thicket owned and controlled by
    /// <paramref name="owner"/>. Shape-only — the cycling activated ability
    /// is attached without an event bus (no <see cref="CardCycledEvent"/>
    /// publication).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Sheltered Thicket. When <paramref name="eventBus"/> is
    /// supplied the cycling resolve body publishes
    /// <see cref="CardCycledEvent"/> so CR 702.32d "Whenever a player
    /// cycles a card" triggers fire.
    /// </summary>
    public static Land Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Cycling {2} — CR 702.32. Routed through the shared CyclingFactory
        // primitive; it appends the DiscardSelfCost hand-zone gate
        // (CR 702.32a) and the CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost(CyclingCost), eventBus);

        return land;
    }
}
