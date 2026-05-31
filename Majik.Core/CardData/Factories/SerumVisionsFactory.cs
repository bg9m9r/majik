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
/// Named-card factory for Serum Visions (Fifth Dawn / Modern Horizons, {U}).
///
/// Sorcery. Oracle text:
///   "Draw a card. Scry 2."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) draws one card
///   via <see cref="Fx.DrawCards"/> (CR 121.1) — so any active replacement
///   effects (Dredge etc.) get a chance on the draw — then runs the
///   standard <see cref="ScryAction"/> pipeline for N=2. When an
///   <see cref="IPlayerAgent"/> is registered via <see cref="AgentRegistry"/>
///   the controller decides the bottom/top partition; otherwise the
///   pre-agent default sends all peeked cards to the bottom (same posture
///   as <see cref="PreordainFactory"/> / <see cref="OptFactory"/>).
/// - Empty library: the draw flags the player for the standard draw-from-
///   empty-library penalty (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>; the trailing
///   scry short-circuits cleanly when the peek returns an empty list.
///
/// ## Notes
/// - Order matters — Serum Visions resolves the draw BEFORE the scry, so
///   the scry inspects the top of the post-draw library. Modern Horizons
///   reprint did not change this clause; canonical Comp Rules treatment
///   is CR 121.1 + CR 701.20 sequenced left-to-right.
/// </summary>
[CardName("Serum Visions")]
public static class SerumVisionsFactory
{
    public const string CardName = "Serum Visions";
    public const string PrintedManaCost = "{U}";
    private const int ScryAmount = 2;

    /// <summary>CardDef DSL — card shape only. <see cref="BuildResolveEffect"/>
    /// supplies the resolve-time "draw a card, then scry 2" body.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Serum Visions' resolve effect — draw a card, then scry 2.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Serum Visions: draw a card, then scry 2.", async ctx =>
            {
                // CR 121.1 — "Draw a card." Route through Fx.DrawCards so a
                // ReplacementBus (Dredge etc.) gets a shot; an empty library
                // flags the SBA-driven loss (CR 704.5b) inside Fx.
                Fx.DrawCards(caster, 1);

                // CR 701.20 — Scry 2. Look at the top two cards; the
                // controller chooses which (if any) to put on the bottom of
                // the library. Sourced from the registered agent when
                // available; the pre-agent default sends everything to the
                // bottom (same fallback as PreordainFactory / OptFactory).
                var peeked = ScryAction.Peek(caster, ScryAmount);
                if (peeked.Count == 0)
                {
                    return;
                }

                var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    decision = (await agent.ChooseScryDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
                }
                else
                {
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                ScryAction.Apply(caster, peeked.Count, decision);
            }),
        };
    }
}
