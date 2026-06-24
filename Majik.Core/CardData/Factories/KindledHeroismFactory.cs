using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kindled Heroism (Tarkir: Dragonstorm, {R}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Target creature gets +1/+0 and gains first strike until end of turn.
///    Scry 1."
///
/// ## Implementation
///
/// The +1/+0 + first-strike-until-end-of-turn half mirrors
/// <see cref="ViolentUrgeFactory"/> (a single 1..1 "target creature"
/// <see cref="TargetRequest"/> that on resolution registers a
/// <see cref="PumpUntilEndOfTurnEffect"/>(+1, +0) at Layer 7c and a
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/>(First strike) at Layer 6);
/// the unconditional "Scry 1." rider reuses the same <see cref="ScryAction"/>
/// shape <see cref="PlayWithFireFactory"/> routes through (peek → agent
/// decision → apply). Both already-supported pieces are composed in a single
/// resolve-time effect.
///
/// Card shape comes from the embedded JSON (<c>kindled-heroism.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema) — same posture as
/// <see cref="ViolentUrgeFactory"/> / <see cref="PlayWithFireFactory"/>.
///
/// On resolution (CR 608.2e — left-to-right clause ordering):
///   1. "Target creature gets +1/+0 and gains first strike until end of turn."
///      When the resolver returns a live <see cref="Creature"/> with an active
///      continuous-effects service, register a
///      <see cref="PumpUntilEndOfTurnEffect"/>(+1, +0) (CR 613.1g Layer 7c) and
///      a <see cref="GrantKeywordUntilEndOfTurnEffect"/>(First strike)
///      (CR 613.1c Layer 6). Both expire in the cleanup step (CR 514.2). An
///      illegal pump target (CR 608.2b) no-ops this clause without throwing.
///   2. "Scry 1." Unconditional — the caster always scrys 1 (CR 701.20),
///      independent of whether the pump clause applied. An empty library
///      short-circuits cleanly.
/// </summary>
[CardName("Kindled Heroism")]
public static class KindledHeroismFactory
{
    public const string CardName = "Kindled Heroism";
    public const string Slug = "kindled-heroism";
    public const string PrintedManaCost = "{R}";

    /// <summary>Layer 7c power bonus (CR 613.1g) — +1.</summary>
    public const int PumpPower = 1;

    /// <summary>Layer 7c toughness bonus (CR 613.1g) — +0.</summary>
    public const int PumpToughness = 0;

    /// <summary>Granted keyword — CR 702.7 First strike (Layer 6, CR 613.1c).</summary>
    public const string GrantedFirstStrike = "First strike";

    /// <summary>CR 701.20 — scry 1.</summary>
    private const int ScryAmount = 1;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Kindled Heroism is
    /// cast. Single 1..1 "target creature" request, no modes, no X. On
    /// resolution: pump the target +1/+0 and grant First strike until end of
    /// turn (CR 514.2), then the caster scrys 1 (CR 701.20).
    /// </summary>
    /// <param name="caster">The player who cast Kindled Heroism; scrys.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Kindled Heroism: target creature gets +1/+0 and first strike until end of turn, then scry 1", async ctx =>
                    {
                        // CR 608.2e step 1 — pump + grant first strike.
                        PumpAndGrantFirstStrike(raw);

                        // CR 608.2e step 2 / CR 701.20 — "Scry 1." Unconditional;
                        // still happens when the pump clause no-ops.
                        await ScryOne(caster, ctx).ConfigureAwait(false);
                    }),
                };
            });
    }

    private static void PumpAndGrantFirstStrike(object raw)
    {
        // CR 608.2b — the pump/grant applies only while the target is still a
        // creature with a live continuous-effects service; otherwise it no-ops.
        if (raw is not Creature target) return;
        if (target.ActiveEffects == null) return;

        // CR 613.1g Layer 7c — +1/+0.
        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));

        // CR 613.1c Layer 6 — keyword grant: First strike (CR 702.7).
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedFirstStrike));
    }

    private static async Task ScryOne(Player caster, ResolutionContext ctx)
    {
        // CR 701.20 — scry 1 for the caster.
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
    }
}
