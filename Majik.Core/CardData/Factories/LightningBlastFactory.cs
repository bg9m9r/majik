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
/// Named-card factory for Lightning Blast (Portal / Portal Second Age,
/// {3}{R}).
///
/// Instant. Oracle text:
///   "Lightning Blast deals 4 damage to any target."
///
/// The four-damage burst — same shape as
/// <see cref="LightningStrikeFactory"/> but at {3}{R} with a 4-damage
/// payload. Routed through <see cref="Fx.DealDamageAny"/> so creature /
/// player / planeswalker / battle targets all resolve correctly (CR 115.3).
/// </summary>
[CardName("Lightning Blast")]
public static class LightningBlastFactory
{
    public const string CardName = "Lightning Blast";
    public const string PrintedManaCost = "{3}{R}";
    public const int Damage = 4;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Lightning Blast
    /// is cast. Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (4) damage to the chosen target through
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
                    Fx.Inline("Lightning Blast: 4 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
