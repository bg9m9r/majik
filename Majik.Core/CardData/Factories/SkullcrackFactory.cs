using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skullcrack (Gatecrash, {1}{R}).
///
/// Instant. Oracle text:
///   "Players can't gain life this turn. Damage can't be prevented this
///    turn. Skullcrack deals 3 damage to target player or planeswalker."
///
/// ## Implemented (v1)
/// - <b>Instant {1}{R}</b> shape via <see cref="CardDef.Instant"/>.
/// - <b>Single 1..1 "target player or planeswalker" request</b> (CR 115.1 —
///   Skullcrack specifically excludes creature targets). On resolution the
///   factory checks the resolved target type: only <see cref="Player"/> and
///   <see cref="Planeswalker"/> receive damage. A creature target (illegal
///   but possible via targeting-zone tricks) no-ops per CR 608.2b ("do as
///   much as you can").
/// - <b>3 damage</b> via <see cref="Fx.DealDamageAny"/> for planeswalker
///   loyalty removal (CR 306.7) and <see cref="Player.LoseLife"/> for
///   player targets.
/// - <b>"Players can't gain life this turn" (CR 614 / CR 119.6)</b> —
///   when a <see cref="ReplacementBus"/> is supplied a
///   <see cref="SkullcrackLifeGainBlocker"/> is registered on resolve; the
///   blocker implements <see cref="IEndOfTurnExpirable"/> so
///   <see cref="ReplacementBus.ExpireEndOfTurn"/> removes it at cleanup
///   (CR 514.2). Without a bus the rider silently no-ops (shape tests).
///
/// ## Deferred (v1 gaps)
/// - <b>"Damage can't be prevented this turn"</b> — no global
///   damage-prevention suppression infrastructure exists in the engine
///   today (prevention effects are per-shield objects checked at
///   <see cref="DamageIntent"/> application time; there is no "prevention
///   flag" on the bus or game state). This clause is a documented no-op in
///   v1. A future PR that adds <c>PreventionSuppressedFlag</c> to the
///   <see cref="ReplacementBus"/> and gates every
///   <see cref="PreventAllCombatDamageShield"/> /
///   <c>PreventNextNDamageToAnyTargetShield</c> on it should also wire
///   Skullcrack here.
/// </summary>
[CardName("Skullcrack")]
public static class SkullcrackFactory
{
    public const string CardName = "Skullcrack";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Skullcrack is
    /// cast. Single 1..1 "target player or planeswalker" request; on
    /// resolution:
    /// <list type="number">
    ///   <item>Registers the "players can't gain life this turn"
    ///         <see cref="SkullcrackLifeGainBlocker"/> on
    ///         <paramref name="replacements"/> (when non-null, EOT-expirable
    ///         per CR 514.2).</item>
    ///   <item>Deals <see cref="Damage"/> (3) damage to the chosen target
    ///         if it is a <see cref="Player"/> or <see cref="Planeswalker"/>
    ///         via <see cref="Fx.DealDamageAny"/>; no-ops otherwise
    ///         (CR 608.2b).</item>
    /// </list>
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="replacements">Optional per-turn replacement bus. When
    /// supplied the "players can't gain life this turn" blocker is
    /// registered as an EOT-expirable replacement effect. When null the
    /// lifegain-prevention clause is a no-op (shape tests).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player or planeswalker", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Skullcrack: no lifegain this turn + 3 damage to player or planeswalker", () =>
                    {
                        // "Players can't gain life this turn" — CR 614 / 119.6.
                        // Register EOT-expirable blocker when a bus is wired.
                        if (replacements != null)
                        {
                            replacements.Register(new SkullcrackLifeGainBlocker());
                        }

                        // "Damage can't be prevented this turn" — DEFERRED v1 no-op.
                        // No global prevention-suppression infrastructure exists;
                        // see XML doc comment above for the future wiring point.

                        // 3 damage to target player or planeswalker (CR 608.2b:
                        // no-op for creature targets — only these two types are legal).
                        if (target is Player || target is Planeswalker)
                        {
                            Fx.DealDamageAny(target, Damage);
                        }
                    }),
                };
            });
    }

    // -----------------------------------------------------------------------
    // Per-turn lifegain blocker
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 614 / 119.6 — "Players can't gain life this turn."
    /// Rewrites every <see cref="LifeGainIntent"/> to zero amount while
    /// registered. Implements <see cref="IEndOfTurnExpirable"/> so
    /// <see cref="ReplacementBus.ExpireEndOfTurn"/> drops it at cleanup
    /// (CR 514.2).
    /// </summary>
    public sealed class SkullcrackLifeGainBlocker
        : IReplacementEffect<LifeGainIntent>, IEndOfTurnExpirable
    {
        public bool OneShot => false;
        public object? Tag => this;
        public bool ExpiresAtEndOfTurn => true;

        public bool Applies(LifeGainIntent intent, IReadOnlyList<object> history) =>
            intent.Amount > 0;

        public LifeGainIntent Replace(LifeGainIntent intent, IReadOnlyList<object> history) =>
            intent with { Amount = 0 };
    }
}
