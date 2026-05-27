using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dissolve (Theros / various reprints, {1}{U}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell. Scry 1."
///
/// Dissolve is Counterspell plus a Scry 1 rider for the caster immediately
/// after the counter resolves (CR 701.5 + CR 701.20). No type filter —
/// any spell on the stack is a legal target.
///
/// ## Implemented
/// - Instant shape, mana cost {1}{U}{U}.
/// - <see cref="BuildSpellDefinition"/> declares a single 1..1 "target spell"
///   request; on resolution removes the target from the stack (CR 701.5),
///   then scries 1 for the caster (CR 701.20).
/// - Scry decision is sourced from the registered <see cref="IPlayerAgent"/>
///   when available; falls back to "all-bottom" when no agent is registered
///   (same posture as <see cref="OptFactory"/> / <see cref="SerumVisionsFactory"/>).
/// - Empty library: scry short-circuits (peek returns empty list) — no throw.
/// </summary>
[CardName("Dissolve")]
public static class DissolveFactory
{
    public const string CardName = "Dissolve";
    public const string PrintedManaCost = "{1}{U}{U}";

    /// <summary>CardDef DSL — card shape only. The counter + scry
    /// SpellDefinition is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target spell, then scry 1" SpellDefinition.
    /// Declares a single 1..1 "target spell" request; on resolution removes
    /// the target from the stack, sends its card to the graveyard (CR 701.5),
    /// and then scries 1 for <paramref name="caster"/> (CR 701.20).
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the counter effect becomes a no-op.</param>
    /// <param name="caster">The player who cast Dissolve. Scry 1 is performed
    /// for this player after the counter.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        Player caster)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Dissolve — counter target spell, then scry 1", () =>
                    {
                        // CR 701.5 — "Counter target spell."
                        if (stack != null && resolved is ISpell spell)
                        {
                            OracleSpellBinder.RemoveFromStack(stack, spell);
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }

                        // CR 701.20 — "Scry 1." Look at the top card; the
                        // controller chooses whether to put it on the bottom
                        // of the library or leave it on top. Decision sourced
                        // from the registered agent when available; otherwise
                        // the pre-agent default sends the peeked card to the
                        // bottom (same fallback as OptFactory / SerumVisionsFactory).
                        var peeked = ScryAction.Peek(caster, 1);
                        if (peeked.Count == 0) return;

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
                    }),
                };
            });
    }
}
