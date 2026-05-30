using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Raugrin Crystal (Modern Horizons 3) — the Jeskai
/// member of the "Crystal" mana-rock cycle. Oracle text (verified against
/// Scryfall):
///   "{T}: Add {U}, {R}, or {W}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// <para>
/// The Artifact shell — mana cost {3} plus the three "{T}: Add" mana
/// abilities {U}/{R}/{W} (CR 605.1 — mana abilities don't use the stack) —
/// is declared declaratively in
/// <c>Majik.Core/CardData/Cards/raugrin-crystal.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. Each <c>{ "kind": "mana" }</c> entry
/// builds a vanilla "{T}: Add &lt;Produces&gt;" <see cref="Abilities.ManaAbility"/>
/// gated on the artifact being untapped — the same JSON shape Raugrin Triome
/// uses, minus the printed land subtypes (this is an Artifact, not a Land, so
/// the tap is a real {T} cost rather than the land's intrinsic tap).
/// </para>
///
/// <para>
/// Cycling {2} (CR 702.32) is attached on top via the shared
/// <see cref="CyclingFactory.Build"/> primitive — same posture as
/// <see cref="RaugrinTriomeFactory"/>. The primitive stamps the
/// <see cref="KeywordAbility"/>("Cycling") marker (so keyword consumers see
/// it) and publishes <see cref="CardCycledEvent"/> on resolve (CR 702.32d).
/// Cycle cost is <see cref="ManaCostCost"/>("2"); the primitive appends the
/// <see cref="DiscardSelfCost"/> hand-zone gate (CR 702.32a).
/// </para>
/// </summary>
[CardName("Raugrin Crystal")]
public static class RaugrinCrystalFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("raugrin-crystal");

    /// <summary>Construct Raugrin Crystal owned and controlled by
    /// <paramref name="owner"/> (shape-only path — cycling does not publish
    /// <see cref="CardCycledEvent"/>).</summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>Construct Raugrin Crystal with optional bus wiring so the
    /// cycling resolve publishes <see cref="CardCycledEvent"/>
    /// (CR 702.32d).</summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        var artifact = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // Cycling {2}. CR 702.32 — "{2}, Discard this card: Draw a card."
        // The shared primitive appends the DiscardSelfCost hand-zone gate
        // (CR 702.32a) and the CardCycledEvent publish (CR 702.32d).
        CyclingFactory.Build(artifact, new ManaCostCost("2"), eventBus);

        return artifact;
    }
}
