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
/// Named-card factory for Shock (Mirage / many reprints, {R}).
///
/// Instant. Oracle text:
///   "Shock deals 2 damage to any target."
///
/// Vanilla "any target" burn — the simplest Bolt-shaped spell at
/// {R} → 2 damage (CR 115.3 — "any target" = creature, player,
/// planeswalker, or battle). Routed through
/// <see cref="Fx.DealDamageAny"/> so all three legal target classes
/// resolve correctly (CR 306.7 — damage to a planeswalker becomes
/// loyalty removal).
///
/// Mirrors the resolve shape used by <see cref="LavaDartFactory"/> and
/// <see cref="BurstLightningFactory"/>'s base branch.
/// </summary>
[CardName("Shock")]
public static class ShockFactory
{
    public const string CardName = "Shock";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 2;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/> (the runtime needs
    /// the caller's target resolver, which lives on the
    /// <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Shock is cast.
    /// Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (2) damage to the chosen target through
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
                    Fx.Inline("Shock: 2 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
