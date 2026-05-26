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
/// Named-card factory for Lightning Bolt (Alpha / many reprints, {R}).
///
/// Instant. Oracle text:
///   "Lightning Bolt deals 3 damage to any target."
///
/// The canonical Bolt-shape burn spell — {R} for 3 damage to any of the
/// four legal target classes (creature, player, planeswalker, battle —
/// CR 115.3). Resolve routes through <see cref="Fx.DealDamageAny"/> so
/// all classes resolve correctly (CR 306.7 — damage to a planeswalker
/// becomes loyalty removal).
///
/// Mirrors the resolve shape used by <see cref="ShockFactory"/> with the
/// damage amount bumped from 2 to 3. <see cref="DamageAnyTargetTemplate"/>
/// already binds Bolt's oracle text generically; this named factory
/// exists so the embedded-seed exporter flags the row
/// <c>IsImplemented = true</c> (the seed reflects over <c>[CardName]</c>
/// factories, not spell-template coverage).
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
