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
/// Named-card factory for Bolt of Keranos (Theros Beyond Death, {1}{R}{R}).
///
/// Sorcery. Oracle text (Scryfall-confirmed):
///   "Bolt of Keranos deals 3 damage to any target. Scry 1. (Look at the top
///    card of your library. You may put that card on the bottom.)"
///
/// Two-part resolve (CR 608.2e — left-to-right clause ordering), identical in
/// shape to <see cref="MagmaJetFactory"/> — only the values differ (sorcery
/// rather than instant, 3 damage rather than 2, scry 1 rather than 2):
///   1. Deal 3 damage to any target (creature, player, planeswalker, battle)
///      via <see cref="Fx.DealDamageAny"/> — same routing as
///      <see cref="LightningStrikeFactory"/> / <see cref="ShockFactory"/>
///      (CR 115.3, CR 120.3).
///   2. Scry 1 for the casting player (CR 701.20). The controller's
///      registered <see cref="IPlayerAgent"/> is consulted when present;
///      the pre-agent default sends the peeked card to the bottom of the
///      library (matching the <see cref="MagmaJetFactory"/> /
///      <see cref="SerumVisionsFactory"/> fallback posture).
/// </summary>
[CardName("Bolt of Keranos")]
public static class BoltOfKeranosFactory
{
    public const string CardName = "Bolt of Keranos";
    public const string PrintedManaCost = "{1}{R}{R}";
    public const int Damage = 3;
    private const int ScryAmount = 1;

    /// <summary>CardDef DSL — card shape only. Damage + scry body is
    /// supplied at cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Bolt of Keranos is cast.
    /// Single 1..1 "any target" request. On resolution:
    ///   1. Deals <see cref="Damage"/> (3) damage to the chosen target via
    ///      <see cref="Fx.DealDamageAny"/> (CR 120.3).
    ///   2. Caster scrys 1 (CR 701.20).
    /// </summary>
    /// <param name="caster">The player who cast Bolt of Keranos; scrys after the
    /// damage clause resolves.</param>
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
                    Fx.Inline("Bolt of Keranos: 3 damage to any target, then scry 1", async ctx =>
                    {
                        // CR 120.3 / CR 608.2e step 1 — deal 3 damage.
                        Fx.DealDamageAny(target, Damage);

                        // CR 701.20 / CR 608.2e step 2 — scry 1 for the caster.
                        var peeked = ScryAction.Peek(caster, ScryAmount);
                        if (peeked.Count == 0)
                        {
                            return; // empty library — scry short-circuits cleanly.
                        }

                        var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                        ScryAction.ScryDecision decision;
                        if (agent != null)
                        {
                            decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            // Pre-agent default: send the peeked card to the bottom.
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
