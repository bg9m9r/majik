using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ketria Crystal (Ikoria: Lair of Behemoths) — the
/// Sultai-wedge member of the "Crystal" artifact mana-rock cycle. Oracle text
/// (Scryfall): Artifact, {3}.
///   "{T}: Add {G}, {U}, or {R}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// <para>
/// The Artifact shell — types + {3} mana cost + the three mana abilities
/// {G}/{U}/{R} (CR 605.1 — mana abilities don't use the stack; each
/// <see cref="Majik.Core.Abilities.ManaAbility"/> taps its source on
/// activation, satisfying the printed "{T}" cost) — is declared declaratively
/// in <c>Majik.Core/CardData/Cards/ketria-crystal.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="ZagothTriomeFactory"/> (the land Triome sibling that shares the
/// "tap for one of three colours + Cycling" shape).
/// </para>
///
/// <para>
/// Cycling {2} (CR 702.32) is attached on top via the shared
/// <see cref="CyclingFactory.Build"/> primitive rather than a JSON
/// activated-ability shape: the primitive also stamps the
/// <see cref="KeywordAbility"/>("Cycling") marker (so keyword consumers see
/// it) and publishes <see cref="CardCycledEvent"/> on resolve (CR 702.32d —
/// "Whenever a player cycles a card"), neither of which the data-only
/// activated-ability schema expresses today. Cycle cost is
/// <see cref="ManaCostCost"/>("2"); the primitive appends the
/// <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a).
/// </para>
/// </summary>
[CardName("Ketria Crystal")]
public static class KetriaCrystalFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ketria-crystal");

    /// <summary>Construct Ketria Crystal owned and controlled by
    /// <paramref name="owner"/> (shape-only path — cycling does not publish
    /// <see cref="CardCycledEvent"/>).</summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>Construct Ketria Crystal with optional bus wiring so the
    /// cycling resolve publishes <see cref="CardCycledEvent"/>
    /// (CR 702.32d).</summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        var crystal = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // Cycling {2}. CR 702.32 — "{2}, Discard this card: Draw a card."
        // The shared primitive appends the DiscardSelfCost hand-zone gate
        // (CR 702.32a) and the CardCycledEvent publish (CR 702.32d).
        CyclingFactory.Build(crystal, new ManaCostCost("2"), eventBus);

        return crystal;
    }
}
