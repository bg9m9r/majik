using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magma Jet (Fifth Dawn / various reprints, {1}{R}).
///
/// Instant. Oracle text:
///   "Magma Jet deals 2 damage to any target. Scry 2."
///
/// Two-part resolve (CR 608.2e — left-to-right clause ordering):
///   1. Deal 2 damage to any target (creature, player, planeswalker, battle)
///      via <see cref="Fx.DealDamageAny"/> — same routing as
///      <see cref="LightningStrikeFactory"/> / <see cref="ShockFactory"/>
///      (CR 115.3, CR 120.3).
///   2. Scry 2 for the casting player (CR 701.20). The controller's
///      registered <see cref="IPlayerAgent"/> is consulted when present;
///      the pre-agent default sends all peeked cards to the bottom of the
///      library (matching the <see cref="SerumVisionsFactory"/> /
///      <see cref="PreordainFactory"/> fallback posture).
/// </summary>
[CardName("Magma Jet")]
public static class MagmaJetFactory
{
    public const string CardName = "Magma Jet";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 2;
    private const int ScryAmount = 2;

    /// <summary>CardDef DSL — card shape only. Damage + scry body is
    /// supplied at cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Magma Jet is cast.
    /// Single 1..1 "any target" request. On resolution:
    ///   1. Deals <see cref="Damage"/> (2) damage to the chosen target via
    ///      <see cref="Fx.DealDamageAny"/> (CR 120.3).
    ///   2. Caster scrys 2 (CR 701.20).
    /// </summary>
    /// <param name="caster">The player who cast Magma Jet; receives the
    /// scry trigger.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="Game.GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Magma Jet: 2 damage to any target, then scry 2", () =>
                    {
                        // CR 120.3 / CR 608.2e step 1 — deal 2 damage.
                        Fx.DealDamageAny(target, Damage);

                        // CR 701.20 / CR 608.2e step 2 — scry 2 for the caster.
                        var peeked = ScryAction.Peek(caster, ScryAmount);
                        if (peeked.Count == 0)
                        {
                            return; // empty library — scry short-circuits cleanly.
                        }

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
                            // Pre-agent default: send all peeked cards to the bottom.
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
