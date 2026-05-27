using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brimstone Volley (Innistrad / various reprints,
/// {2}{R}).
///
/// Instant. Oracle text:
///   "Brimstone Volley deals 3 damage to any target.
///    Morbid — Brimstone Volley deals 5 damage instead if a creature died
///    this turn."
///
/// ## Implementation
/// - Lightning-Strike-shape burn: single 1..1 "any target"
///   <see cref="TargetRequest"/>; resolves via <see cref="Fx.DealDamageAny"/>
///   so creature / player / planeswalker / battle targets all work
///   (CR 115.3).
/// - Morbid (CR 700.6 — "a creature died this turn") sampled from
///   <see cref="TurnState.CreaturesDiedThisTurn"/> at resolution time.
///   When no <see cref="TurnState"/> is wired (shape / dispatcher tests)
///   Morbid is treated as inactive (base 3-damage clause applies) — same
///   posture as <see cref="TragicSlipFactory"/> and
///   <see cref="FatalPushFactory"/>.
/// - Damage = Morbid ? <see cref="MorbidDamage"/> (5) :
///   <see cref="BaseDamage"/> (3).
/// </summary>
[CardName("Brimstone Volley")]
public static class BrimstoneVolleyFactory
{
    public const string CardName = "Brimstone Volley";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>Base damage (CR 700.6 — Morbid not active).</summary>
    public const int BaseDamage = 3;

    /// <summary>Morbid-upgraded damage — a creature died this turn.</summary>
    public const int MorbidDamage = 5;

    /// <summary>CardDef DSL — card shape only. Morbid-gated damage body lives
    /// in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Brimstone Volley is
    /// cast. Single 1..1 "any target" request; on resolution the per-turn
    /// creature-death tally is sampled (Morbid) and the damage swapped from
    /// 3 → 5 when active.
    /// </summary>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at resolution time. Null return (no driver
    /// wired — typical for shape / dispatcher tests) is treated as Morbid
    /// inactive (base 3 damage). Same posture as
    /// <see cref="TragicSlipFactory.BuildSpellDefinition"/>.</param>
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
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = targetResolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: {BaseDamage} damage to any target ({MorbidDamage} with Morbid)",
                        () =>
                        {
                            // CR 700.6 — Morbid: "a creature died this turn."
                            var damage = IsMorbidActive(turnStateResolver)
                                ? MorbidDamage
                                : BaseDamage;

                            // CR 115.3 — "any target": creature, player,
                            // planeswalker, or battle. DealDamageAny handles
                            // the dispatch.
                            Fx.DealDamageAny(target, damage);
                        }),
                };
            });
    }

    /// <summary>
    /// Sample whether Morbid is active (CR 700.6 — at least one creature
    /// died this turn). Returns false when no <see cref="TurnState"/> is
    /// wired — same posture as <see cref="TragicSlipFactory.IsMorbidActive"/>.
    /// </summary>
    public static bool IsMorbidActive(Func<TurnState?> turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        var turnState = turnStateResolver.Invoke();
        return turnState != null && turnState.CreaturesDiedThisTurn > 0;
    }
}
