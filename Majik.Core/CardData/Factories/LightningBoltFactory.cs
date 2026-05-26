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
/// Named-card factory for Lightning Bolt (Alpha / countless reprints, {R}).
///
/// Instant. Oracle text:
///   "Lightning Bolt deals 3 damage to any target."
///
/// The archetypal one-mana burn spell — same resolve shape as
/// <see cref="ShockFactory"/> but for 3 damage instead of 2. Routes through
/// <see cref="Fx.DealDamageAny"/> so all four "any target" classes resolve
/// correctly (CR 115.3 — creature, player, planeswalker, or battle).
/// CR 306.7 — damage to a planeswalker becomes loyalty removal; CR 309.5 —
/// damage to a battle becomes defense removal; both are handled inside
/// <see cref="Fx.DealDamageAny"/>.
/// </summary>
[CardName("Lightning Bolt")]
public static class LightningBoltFactory
{
    public const string CardName = "Lightning Bolt";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/> (the runtime needs
    /// the caller's target resolver, which lives on the
    /// <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Lightning Bolt is
    /// cast. Single 1..1 "any target" request; on resolution deals
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
                    Fx.Inline("Lightning Bolt: 3 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
