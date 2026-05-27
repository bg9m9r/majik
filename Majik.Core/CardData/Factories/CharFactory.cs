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
/// Named-card factory for Char (Portal Second Age / Tempest Remastered,
/// {2}{R}).
///
/// Instant. Oracle text:
///   "Char deals 4 damage to any target and 2 damage to you."
///
/// ## Implementation
///
/// Two-part resolution (CR 608.2):
///   1. <see cref="Fx.DealDamageAny"/> deals 4 damage to the chosen "any
///      target" (Player / Creature / Planeswalker — CR 115.3).
///   2. <see cref="Fx.DealDamage"/> deals 2 damage to the caster (the
///      "you" in the oracle text — CR 119.2). Routing through
///      <see cref="Fx.DealDamage"/> (which calls
///      <see cref="OracleSpellBinder.DealDamage"/> → Player.LoseLife)
///      ensures the engine's standard player-damage path is used, keeping
///      the event consistent with other burn sources.
///
/// The self-damage is unconditional — it always happens on resolution
/// regardless of whether the any-target becomes illegal (fizzle only
/// applies when ALL targets are illegal; if the target fizzles, Char
/// fizzles and the caster takes no damage — mirroring Lightning Strike's
/// posture, CR 608.2b). The caster receives 2 damage at the same
/// resolution step, not as a separate triggered ability.
///
/// Card-shape only here; the resolve-time spell definition is built
/// on-demand via <see cref="BuildSpellDefinition(Player, Func{object,object})"/>
/// because <see cref="SpellDefinition"/> needs a target resolver supplied
/// by the caller's <see cref="GameContext"/>.
/// </summary>
[CardName("Char")]
public static class CharFactory
{
    public const string CardName = "Char";
    public const string PrintedManaCost = "{2}{R}";
    public const int TargetDamage = 4;
    public const int SelfDamage = 2;

    /// <summary>CardDef DSL — card shape only. Dual-damage resolve body is
    /// supplied at cast time via
    /// <see cref="BuildSpellDefinition(Player, Func{object,object})"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Char is cast.
    /// Single 1..1 "any target" request; on resolution deals
    /// <see cref="TargetDamage"/> (4) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/>, then deals <see cref="SelfDamage"/>
    /// (2) damage to <paramref name="caster"/> through
    /// <see cref="Fx.DealDamage"/> (CR 119 — player damage path).
    /// </summary>
    /// <param name="caster">The spell's controller — takes 2 damage on
    /// resolution ("and 2 damage to you").</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game
    /// object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
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
                    Fx.Inline("Char: 4 damage to any target, 2 damage to you", () =>
                    {
                        Fx.DealDamageAny(target, TargetDamage);
                        Fx.DealDamage(caster, SelfDamage);
                    }),
                };
            });
    }
}
