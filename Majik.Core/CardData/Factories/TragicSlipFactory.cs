using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tragic Slip (Dark Ascension / various reprints, {B}).
///
/// Instant. Oracle text:
///   "Target creature gets -1/-1 until end of turn.
///    Morbid — That creature gets -13/-13 until end of turn instead if a
///    creature died this turn."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}, black.
/// - Resolve via <see cref="BuildSpellDefinition"/>: single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution:
///   1. Target must still be a Creature on the Battlefield (CR 608.2b —
///      illegal-target filter at resolve, no-op otherwise).
///   2. Sample Morbid (CR 700.6 / "a creature died this turn") from
///      <see cref="TurnState.CreaturesDiedThisTurn"/>. When no
///      <see cref="TurnState"/> is wired (shape / dispatch tests), Morbid
///      is treated as inactive (base clause applies). Same posture as
///      <see cref="FatalPushFactory.IsRevoltActive"/>.
///   3. Amount = Morbid ? -13/-13 : -1/-1 — register a
///      <see cref="PumpUntilEndOfTurnEffect"/> on the target's
///      <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at EOT).
///      Same Layer 7c pump shape as <see cref="DisfigureFactory"/>; the
///      Morbid magnitude is just the conditional value supplied to the same
///      effect. When ActiveEffects is null (shape-only tests without a live
///      ContinuousEffectsService) the registration is a silent no-op.
///
/// Coverage note: the data-driven pump templates bind Tragic Slip's first
/// sentence ("-1/-1 until end of turn"); this named factory exists to wire
/// the Morbid upgrade so production casts see the full printed effect (and
/// the bot's removal range expands once a creature has died this turn).
/// </summary>
[CardName("Tragic Slip")]
public static class TragicSlipFactory
{
    public const string CardName = "Tragic Slip";
    public const string PrintedManaCost = "{B}";

    /// <summary>Base "-N/-N until end of turn" magnitude.</summary>
    public const int BasePenalty = -1;

    /// <summary>Morbid-upgraded "-N/-N until end of turn" magnitude.</summary>
    public const int MorbidPenalty = -13;

    /// <summary>CardDef DSL — card shape only. Morbid-gated pump body lives
    /// in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Tragic Slip is
    /// cast. Single 1..1 target creature; on resolution the per-turn
    /// creature-death tally is sampled (Morbid) and the pump magnitude
    /// swapped from -1/-1 → -13/-13 when active.
    /// </summary>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at resolution time. Null return (no driver
    /// wired — typical for shape / dispatcher tests) is treated as Morbid
    /// inactive (base -1/-1). Same posture as
    /// <see cref="FatalPushFactory.BuildSpellDefinition"/>.</param>
    /// <param name="targetResolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<TurnState?> turnStateResolver,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: enumerate every creature live; the
                    // bot ranks via Removal intent. Mirrors DisfigureFactory.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: target creature gets {BasePenalty}/{BasePenalty} until end of turn ({MorbidPenalty}/{MorbidPenalty} with Morbid)",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 700.6 — Morbid: "a creature died this turn."
                            var amount = IsMorbidActive(turnStateResolver)
                                ? MorbidPenalty
                                : BasePenalty;

                            // CR 514.2 — Layer 7c -N/-N pump, expires at EOT.
                            // Same pattern as DisfigureFactory. ActiveEffects
                            // null (shape tests) → silent no-op.
                            if (target.ActiveEffects == null) return;
                            target.ActiveEffects.Register(
                                new PumpUntilEndOfTurnEffect(target, amount, amount));
                        }),
                };
            });
    }

    /// <summary>
    /// Sample whether Morbid is active (CR 700.6 — at least one creature died
    /// this turn). Returns false when no <see cref="TurnState"/> is wired
    /// (typical for shape / dispatcher tests) — same posture as
    /// <see cref="FatalPushFactory.IsRevoltActive"/>.
    /// </summary>
    public static bool IsMorbidActive(Func<TurnState?> turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        var turnState = turnStateResolver.Invoke();
        return turnState != null && turnState.CreaturesDiedThisTurn > 0;
    }
}
