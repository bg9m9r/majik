using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Indatha Crystal (Ikoria: Lair of Behemoths) — the
/// Abzan ({W}{B}{G}) member of the "Crystal" mana-rock cycle. Oracle text
/// (verified against Scryfall):
///   "{T}: Add {W}, {B}, or {G}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// <para>
/// The Artifact shell — type Artifact, mana cost {3}, plus the three
/// colour-specific mana abilities {W}/{B}/{G} (CR 605.1 — mana abilities
/// don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/indatha-crystal.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="ZagothTriomeFactory"/> (which carries the identical
/// "{T}: Add {W}, {B}, or {G}" mana clause, just on a Land shell). The
/// activator picks a colour by picking the matching mana-ability slot, so no
/// separate colour prompt is needed (CR 605.1).
/// </para>
///
/// <para>
/// Cycling {2} (CR 702.32) is attached on top via the shared
/// <see cref="CyclingFactory.Build"/> primitive rather than the JSON
/// activated-ability shape: the primitive also stamps the
/// <see cref="KeywordAbility"/>("Cycling") marker (so keyword consumers see
/// it) and publishes <see cref="CardCycledEvent"/> on resolve (CR 702.32d —
/// "Whenever a player cycles a card"), neither of which the data-only
/// activated-ability schema expresses today. Cycle cost is
/// <see cref="ManaCostCost"/>("2"); the primitive appends the
/// <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a).
/// </para>
/// </summary>
[CardName("Indatha Crystal")]
public static class IndathaCrystalFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("indatha-crystal");

    /// <summary>Construct Indatha Crystal owned and controlled by
    /// <paramref name="owner"/> (shape-only path — cycling does not publish
    /// <see cref="CardCycledEvent"/>).</summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>Construct Indatha Crystal with optional bus wiring so the
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
