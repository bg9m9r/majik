using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Force of Negation (Modern Horizons, {1}{U}{U}).
///
/// Instant. Oracle text:
///   "If it's not your turn, you may exile a blue card from your hand
///    rather than pay this spell's mana cost.
///    Counter target noncreature spell."
///
/// Implemented in v1:
///   * Instant card shape ({1}{U}{U}, Blue).
///   * Counter target noncreature spell — <see cref="BuildDefinition"/>
///     builds a SpellDefinition whose effect ignores creature spells at
///     resolution time (CR 608.2b — illegal targets cause the effect to do
///     nothing for that target).
///   * Pitch alternative cost (<see cref="Majik.Core.Costs.PitchAlternativeCost"/>):
///     not-your-turn + exile a blue card from hand. No life rider.
///   * Bot probe — <see cref="PitchAltCostProbe"/> recognizes this card by
///     name and emits a candidate per blue card in hand.
/// </summary>
public static class ForceOfNegationFactory
{
    public const string CardName = "Force of Negation";

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, "{1}{U}{U}");
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>Build the "counter target noncreature spell" SpellDefinition.
    /// Mirrors <c>CounterSpellFactory.CounterTypedSpell(requireNonCreature: true)</c>
    /// — inlined here so the named-card factory is fully self-contained.</summary>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver, Majik.Core.Stack.Stack? stack) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target noncreature spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Force of Negation — counter target noncreature spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;
                        // CR 608.2b — if the target is illegal at resolution
                        // (i.e. a creature spell), do nothing.
                        if (spell.Card.HasType(CardType.Creature)) return;
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
}
