using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

internal static class CounterSpellFactory
{
    internal static SpellDefinition CounterTypedSpell(
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack,
        bool requireCreature = false,
        bool requireNonCreature = false) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("counter target typed spell", () =>
            {
                if (stack == null || target is not ISpell spell) return;
                var isCreature = spell.Card.HasType(Majik.Core.Cards.Types.CardType.Creature);
                if (requireCreature && !isCreature) return;
                if (requireNonCreature && isCreature) return;
                OracleSpellBinder.RemoveFromStack(stack, spell);
                spell.Card.SetZone(ZoneType.Graveyard);
            }) };
        });

    internal static SpellDefinition CounterTargetSpell(Func<object, object> resolver, Majik.Core.Stack.Stack? stack) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("counter target spell", () =>
            {
                if (stack == null || target is not ISpell spell) return;
                OracleSpellBinder.RemoveFromStack(stack, spell);
                spell.Card.SetZone(ZoneType.Graveyard);
            }) };
        });
}
