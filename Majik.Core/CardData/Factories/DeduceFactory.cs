using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Deduce (Murders at Karlov Manor, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Draw a card. Investigate. (Create a Clue token. It's an artifact with
///    "{2}, Sacrifice this token: Draw a card.")"
///
/// ## Why it gets its own factory
/// Deduce is the blue half of the cantrip-plus-Clue cycle — a one-and-a-half
/// mana instant that replaces itself and leaves a Clue behind for a second
/// draw later. Both halves are already cleanly modelled by existing
/// primitives: the draw via <see cref="Fx.DrawCards"/> (the same draw-loop
/// used by <see cref="JacesIngenuityFactory.BuildResolveEffect"/>) and the
/// Investigate clause via <see cref="TokenFactory.CreateClue"/> (the shared
/// Clue primitive used by <see cref="NoviceInspectorFactory"/> /
/// <see cref="HardEvidenceFactory"/> / Thraben Inspector). The named factory
/// is a thin composition of the two — no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue. Card shape comes from the embedded
///   JSON (<c>deduce.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>Resolve</b>: the caster draws one card (CR 121.1) via
///   <see cref="Fx.DrawCards"/> (per-draw replacement bus; empty library
///   stamps the SBA loss flag — CR 704.5b — without throwing), then
///   investigates (CR 701.39): one Clue token is created under the caster
///   (a colourless artifact with "{2}, Sacrifice this token: Draw a card.")
///   via <see cref="TokenFactory.CreateClue"/>. Resolution order matches the
///   printed text: draw first, then investigate. No targets, no additional
///   cost.
///
/// Both clauses are fully expressed by the existing engine, so there are no
/// deferred gaps.
///
/// ## Rules citations
/// - CR 121.1 — "Draw a card."
/// - CR 701.39 — Investigate (create a Clue token).
/// </summary>
[CardName("Deduce")]
public static class DeduceFactory
{
    public const string CardName = "Deduce";
    public const string Slug = "deduce";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>CR 121.1 — "Draw a card."</summary>
    public const int DrawAmount = 1;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build Deduce's resolve effect: the caster draws one card (CR 121.1),
    /// then investigates (CR 701.39 — create one Clue token under the caster).
    /// No targets, no additional cost.
    /// </summary>
    /// <param name="caster">The player who cast Deduce; draws the card and
    /// receives the Clue (CR 701.39 — "you create a Clue token").</param>
    /// <param name="zoneService">Optional zone service — routes the Clue ETB
    /// through <see cref="ZoneService"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (enabling
    /// downstream triggers like Tireless Tracker). Null → direct zone move,
    /// suitable for unit-test / shape-only paths.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: draw a card and investigate (create a Clue token).",
                () =>
                {
                    // CR 121.1 — draw 1. Replacement bus per-draw; empty
                    // library stamps the SBA loss flag (CR 704.5b).
                    Fx.DrawCards(caster, DrawAmount);

                    // CR 701.39 — Investigate: create one Clue token under the
                    // caster (a colourless artifact with the "{2}, Sacrifice
                    // this token: Draw a card." activated ability).
                    TokenFactory.CreateClue(caster, zoneService);
                }),
        };
    }
}
