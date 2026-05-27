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
/// Named-card factory for Bombard (Ixalan / reprints, {2}{R}).
///
/// Instant. Oracle text:
///   "Bombard deals 4 damage to target creature."
///
/// Same 4-to-creature payload as <see cref="MizziumMortarsFactory"/> but
/// Bombard is an Instant at {2}{R} rather than a Sorcery. The effect is
/// routed through <see cref="Fx.DealDamageAny"/>; a non-creature resolved
/// target is a no-op (CR 608.2b).
/// </summary>
[CardName("Bombard")]
public static class BombardFactory
{
    public const string CardName = "Bombard";
    public const string PrintedManaCost = "{2}{R}";
    public const int Damage = 4;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Bombard is cast.
    /// Single 1..1 "target creature" request; on resolution deals
    /// <see cref="Damage"/> (4) damage to the chosen target creature through
    /// <see cref="Fx.DealDamageAny"/>. If the resolved target is not a
    /// creature the effect is a no-op (CR 608.2b).
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
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Bombard: 4 damage to target creature", () =>
                    {
                        if (target is not Creature) return;
                        Fx.DealDamageAny(target, Damage);
                    }),
                };
            });
    }
}
