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
/// Named-card factory for Lightning Strike (Magic 2015 / many reprints,
/// {1}{R}).
///
/// Instant. Oracle text:
///   "Lightning Strike deals 3 damage to any target."
///
/// The "off-by-one" Bolt — same 3-damage payload as
/// <see cref="LightningBoltFactory"/> but at {1}{R} instead of {R}. Same
/// resolve shape as <see cref="ShockFactory"/> / <see cref="LightningBoltFactory"/>,
/// routed through <see cref="Fx.DealDamageAny"/> so creature / player /
/// planeswalker / battle targets all resolve correctly (CR 115.3).
/// </summary>
[CardName("Lightning Strike")]
public static class LightningStrikeFactory
{
    public const string CardName = "Lightning Strike";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Lightning Strike
    /// is cast. Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (3) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
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
                    Fx.Inline("Lightning Strike: 3 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
