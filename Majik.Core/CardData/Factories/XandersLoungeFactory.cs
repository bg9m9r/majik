using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Xander's Lounge (Streets of New Capenna) — the
/// Island Swamp Mountain triland ("Triome") cycle. Oracle text:
///   "({T}: Add {U}, {B}, or {R}.)
///    This land enters tapped.
///    Cycling {3} ({3}, Discard this card: Draw a card.)"
///
/// <para>
/// The Land shell — all three printed land subtypes (Island / Swamp /
/// Mountain) plus the three mana abilities {U}/{B}/{R} (CR 605.1 — mana
/// abilities don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/xanders-lounge.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="KetriaTriomeFactory"/>.
/// </para>
///
/// <para>
/// Cycling {3} (CR 702.32) is attached on top via the shared
/// <see cref="CyclingFactory.Build"/> primitive rather than the JSON
/// activated-ability shape: the primitive also stamps the
/// <see cref="KeywordAbility"/>("Cycling") marker (so keyword consumers see
/// it) and publishes <see cref="CardCycledEvent"/> on resolve (CR 702.32d —
/// "Whenever a player cycles a card"), neither of which the data-only
/// activated-ability schema expresses today. Cycle cost is
/// <see cref="ManaCostCost"/>("3"); the primitive appends the
/// <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a).
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as <see cref="KetriaTriomeFactory"/>).
/// </para>
/// </summary>
[CardName("Xander's Lounge")]
public static class XandersLoungeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("xanders-lounge");

    /// <summary>Construct Xander's Lounge owned and controlled by
    /// <paramref name="owner"/> (shape-only path — cycling does not publish
    /// <see cref="CardCycledEvent"/>).</summary>
    public static Land Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>Construct Xander's Lounge with optional bus wiring so the
    /// cycling resolve publishes <see cref="CardCycledEvent"/>
    /// (CR 702.32d).</summary>
    public static Land Create(Player owner, IEventBus? eventBus)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Cycling {3}. CR 702.32 — "{3}, Discard this card: Draw a card."
        // The shared primitive appends the DiscardSelfCost hand-zone gate
        // (CR 702.32a) and the CardCycledEvent publish (CR 702.32d).
        CyclingFactory.Build(land, new ManaCostCost("3"), eventBus);

        return land;
    }
}
