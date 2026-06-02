using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Behold the Multiverse (Kaldheim, {3}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-02):
///   "Scry 2, then draw two cards.
///    Foretell {1}{U} (During your turn, you may pay {2} and exile this
///    card from your hand face down. Cast it on a later turn for its
///    foretell cost.)"
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {3}{U} (mana value 4). The base
///   card shape (name / Instant type / {3}{U} cost) is materialised from the
///   embedded JSON definition (<c>behold-the-multiverse.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="DeadlyDisputeFactory"/>.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/>): scry 2 then draw
///   two cards, sequenced left-to-right exactly like
///   <see cref="PreordainFactory"/> ("Scry 2, then draw a card") — the only
///   delta is the draw count (two instead of one).
///   - <b>Scry 2 (CR 701.20)</b> — look at the top two cards; the controller
///     chooses which (if any) to put on the bottom of the library. Sourced
///     from the registered <see cref="IPlayerAgent"/> via
///     <see cref="AgentRegistry"/> when available; the pre-agent default
///     sends every peeked card to the bottom (same fallback as Preordain /
///     Serum Visions). An empty library short-circuits the scry cleanly
///     (peek returns an empty list).
///   - <b>Draw two (CR 121.1)</b> — routed through <see cref="Fx.DrawCards"/>
///     so any active replacement effect (Dredge etc.) gets a shot per draw;
///     a library that empties mid-draw flags the SBA-driven loss
///     (CR 704.5b) via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///     inside Fx without throwing.
///
/// ## Deferred (v1 gap) — Foretell {1}{U} (CR 702.143)
///
/// This engine does not yet expose the Foretell alternative cost on the cast
/// pipeline, so this factory ships <b>without the foretell alt cost</b>:
/// callers can only cast Behold the Multiverse for its printed {3}{U} mana
/// cost. Same accepted posture as <see cref="DoomskarFactory"/> — Foretell
/// needs the activated-from-hand exile-face-down primitive plus the
/// cast-from-exile pipeline billing the printed foretell cost on a later
/// turn (CR 702.143b–c), neither of which the cast pipeline wires today.
/// The resolve body is identical whether cast for {3}{U} or (eventually) for
/// the foretold {1}{U}, so once Foretell lands the only thing to add is the
/// alt-cost surface; <see cref="BuildResolveEffect"/> stays put.
///
/// (defer: foretell alternative cost — CR 702.143. The factory exposes only
/// the printed {3}{U} mana-cost path; the foretold {1}{U} cast path is not
/// yet available because the cast pipeline lacks the foretell
/// exile-face-down primitive.)
///
/// ## Rules citations
/// - CR 117.5 — printed mana cost.
/// - CR 701.20 — Scry.
/// - CR 121.1 — Draw two cards.
/// - CR 704.5b — draw-from-empty-library loss.
/// - CR 702.143 — Foretell (not yet implemented).
/// </summary>
[CardName("Behold the Multiverse")]
public static class BeholdTheMultiverseFactory
{
    public const string CardName = "Behold the Multiverse";
    public const string PrintedManaCost = "{3}{U}";

    /// <summary>Foretell cost (CR 702.143) — not yet implemented. Held as a
    /// constant for the future cast-pipeline binding.</summary>
    public const string ForetellPrintedCost = "{1}{U}";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "behold-the-multiverse";

    private const int ScryAmount = 2;
    private const int DrawAmount = 2;

    /// <summary>
    /// Build Behold the Multiverse from the embedded JSON and return the
    /// Instant shape. The "scry 2, then draw two" resolve body is built on
    /// demand via <see cref="BuildResolveEffect"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build Behold the Multiverse's resolve effect — scry 2, then draw two
    /// cards (CR 701.20 then CR 121.1, sequenced left-to-right).
    /// </summary>
    /// <param name="caster">Behold the Multiverse's controller; performs the
    /// scry and draws the two cards.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Behold the Multiverse: scry 2, then draw two cards.", async ctx =>
            {
                // CR 701.20 — Scry 2. Look at the top two cards; the
                // controller chooses which (if any) to put on the bottom of
                // the library. Sourced from the registered agent when
                // available; the pre-agent default sends everything to the
                // bottom (same fallback as PreordainFactory / SerumVisions).
                var peeked = ScryAction.Peek(caster, ScryAmount);
                if (peeked.Count > 0)
                {
                    var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                    ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute becomes async.
                        decision = (await agent.ChooseScryDecisionAsync(ctx.Game, peeked).ConfigureAwait(false));
                    }
                    else
                    {
                        decision = new ScryAction.ScryDecision(
                            ToBottom: peeked.ToList(),
                            TopOrder: Array.Empty<ICard>());
                    }
                    ScryAction.Apply(caster, peeked.Count, decision);
                }

                // CR 121.1 — "draw two cards." Route through Fx.DrawCards so a
                // ReplacementBus (Dredge etc.) gets a shot per draw; a library
                // that empties mid-draw flags the SBA-driven loss (CR 704.5b)
                // inside Fx without throwing.
                Fx.DrawCards(caster, DrawAmount);
            }),
        };
    }
}
