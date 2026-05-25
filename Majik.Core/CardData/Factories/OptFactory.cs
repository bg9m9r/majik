using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Opt (Invasion / Ixalan / Modern Horizons 3, {U}).
///
/// Instant. Oracle text:
///   "Look at the top card of your library. You may put that card on the
///    bottom of your library. Draw a card."
///
/// Effectively Scry 1 + draw 1 — pre-Theros Scry templating, just spelled
/// out. We reuse the standard <see cref="ScryAction"/> pipeline for the
/// peek-and-bottom decision so agents that already implement
/// <see cref="IPlayerAgent.ChooseScryDecisionAsync"/> get the right
/// behaviour for free.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) runs the standard
///   <see cref="ScryAction"/> path for N=1 — when an
///   <see cref="IPlayerAgent"/> is registered via <see cref="AgentRegistry"/>
///   the controller decides whether to bottom the peeked card; otherwise the
///   pre-agent default sends the peeked card to the bottom (same default
///   posture as <see cref="PreordainFactory"/>). Then the caster draws one
///   card.
/// - Empty library: scry short-circuits (peek returns an empty list) and
///   the subsequent draw flags the player for the standard
///   draw-from-empty-library penalty (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
///
/// ## Deferred (v1 gaps)
/// - Bot-side scry decision quality lives in the agent implementations
///   (<see cref="HeuristicBotAgent"/> / <see cref="DeterministicBotAgent"/>);
///   this factory just consults whichever agent is registered.
/// </summary>
[CardName("Opt")]
public static class OptFactory
{
    public const string CardName = "Opt";
    public const string PrintedManaCost = "{U}";

    /// <summary>CardDef DSL — card shape only. Look-and-bottom-then-draw body
    /// lives in <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Opt's resolve effect — peek top 1, optionally bottom it, then
    /// draw a card. Returns a single <see cref="IEffect"/> entry so callers
    /// can splice it into a <c>SpellDefinition.EffectFactory</c> result or a
    /// <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Opt: look at top 1, may bottom, then draw a card.", () =>
            {
                // CR 701.20 (functionally) — Look at the top card; the
                // controller chooses whether to put it on the bottom of
                // the library or leave it on top. Decision is sourced from
                // the registered agent when available, falling back to the
                // pre-agent default (bottom-the-peeked) when none is
                // registered — same posture as LibrarySpellFactory.ScryNSpell
                // / PreordainFactory.
                var peeked = ScryAction.Peek(caster, 1);
                if (peeked.Count > 0)
                {
                    var agent = AgentRegistry.Get(caster);
                    ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute becomes async.
                        decision = agent.ChooseScryDecisionAsync(null, peeked)
                            .GetAwaiter().GetResult();
                    }
                    else
                    {
                        decision = new ScryAction.ScryDecision(
                            ToBottom: peeked.ToList(),
                            TopOrder: Array.Empty<ICard>());
                    }
                    ScryAction.Apply(caster, peeked.Count, decision);
                }

                // "Draw a card." Simple top-of-library draw; empty library
                // flags the player for the SBA-driven loss (CR 704.5b) via
                // MarkTriedToDrawFromEmptyLibrary.
                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    caster.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }),
        };
    }
}
