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
/// Named-card factory for Lava Axe (Portal / many reprints, {4}{R}).
///
/// Sorcery. Oracle text:
///   "Lava Axe deals 5 damage to target player or planeswalker."
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {4}{R} (CardDef DSL).
/// - Single 1..1 target request, "target player or planeswalker"
///   (CR 115.1 — Lava Axe specifically excludes creature targets).
/// - On resolution deals <see cref="Damage"/> (5) damage to the chosen
///   target if it is a <see cref="Player"/> or <see cref="Planeswalker"/>
///   via <see cref="Fx.DealDamageAny"/>, which routes planeswalker damage
///   as loyalty removal (CR 306.7). If the resolved target is anything
///   else (e.g. a creature via a Spellskite redirect), the effect
///   no-ops per CR 608.2b.
/// </summary>
[CardName("Lava Axe")]
public static class LavaAxeFactory
{
    public const string CardName = "Lava Axe";
    public const string PrintedManaCost = "{4}{R}";
    public const int Damage = 5;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Lava Axe is cast.
    /// Single 1..1 "target player or planeswalker" request; on resolution
    /// deals <see cref="Damage"/> (5) damage through
    /// <see cref="Fx.DealDamageAny"/> if the target is a
    /// <see cref="Player"/> or <see cref="Planeswalker"/>; no-ops otherwise
    /// (CR 608.2b).
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
                new TargetRequest("target player or planeswalker", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Lava Axe: 5 damage to player or planeswalker", () =>
                    {
                        // CR 608.2b — only Player and Planeswalker are legal targets;
                        // no-op for any other resolved type (e.g. creature via redirect).
                        if (target is Player || target is Planeswalker)
                        {
                            Fx.DealDamageAny(target, Damage);
                        }
                    }),
                };
            });
    }
}
