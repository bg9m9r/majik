using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Searing Spear (Magic 2013, {1}{R}).
///
/// Instant. Oracle text:
///   "Searing Spear deals 3 damage to any target."
///
/// Functional reprint of <see cref="LightningStrikeFactory"/> — same {1}{R}
/// cost and 3-damage "any target" payload, routed through
/// <see cref="Fx.DealDamageAny"/> so creature / player / planeswalker / battle
/// targets all resolve correctly (CR 115.3).
/// </summary>
[CardName("Searing Spear")]
public static class SearingSpearFactory
{
    public const string CardName = "Searing Spear";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 3;

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/>: single 1..1 "any target"
    /// request; on resolution deals <see cref="Damage"/> (3) to the chosen
    /// target through <see cref="Fx.DealDamageAny"/>.
    /// </summary>
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
                    Fx.Inline("Searing Spear: 3 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
