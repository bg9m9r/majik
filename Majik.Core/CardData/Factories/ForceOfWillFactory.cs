using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Force of Will (Alliances, {3}{U}{U}).
///
/// Instant. Oracle text:
///   "You may pay 1 life and exile a blue card from your hand rather than
///    pay this spell's mana cost.
///    Counter target spell."
///
/// Implemented in v1:
///   * Instant card shape ({3}{U}{U}, Blue) — built via the fluent
///     <see cref="CardDef"/> DSL.
///   * Counter target spell — <see cref="SpellDefinition"/> built by
///     <see cref="BuildDefinition"/>; binds via the same target-spell +
///     push-to-graveyard idiom used by <c>CounterTargetSpellTemplate</c>.
///   * Pitch alternative cost (<see cref="Majik.Core.Costs.PitchAlternativeCost"/>):
///     not-your-turn + exile a blue card from hand + lose 1 life.
///   * Bot probe — <see cref="PitchAltCostProbe"/> recognizes this card.
///
/// Reminder: the Force-of-Will pitch is CR 118.9 (alternative cost).
/// </summary>
[CardName("Force of Will")]
public static class ForceOfWillFactory
{
    public const string CardName = "Force of Will";

    public static CardDef Define() => CardDef.Instant(CardName, "{3}{U}{U}");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>Build the "counter target spell" SpellDefinition for Force of Will.
    /// Mirrors <c>CounterSpellFactory.CounterTargetSpell</c> — kept inline here so
    /// the named-card factory is fully self-contained.</summary>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver, Majik.Core.Stack.Stack? stack) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Force of Will — counter target spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
}
