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

    /// <summary>
    /// "Counter target [type] spell unless its controller pays {N}." —
    /// Spell Pierce / Mana Tithe / Mana Leak shape. The rider is modeled as
    /// an automatic mana-pool consult on resolution: if the target spell's
    /// controller has {N} generic mana in their pool, it's spent and the
    /// counter no-ops (the spell resolves); otherwise the spell is countered
    /// per CR 701.5 / CR 118.4. <paramref name="requireCreature"/> and
    /// <paramref name="requireNonCreature"/> let the same factory cover
    /// hypothetical typed variants alongside the plain "target spell" form.
    /// </summary>
    internal static SpellDefinition CounterTargetSpellUnlessPay(
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack,
        int unlessPayN,
        bool requireCreature = false,
        bool requireNonCreature = false) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("counter target spell unless its controller pays", () =>
            {
                if (stack == null || target is not ISpell spell) return;
                var isCreature = spell.Card.HasType(Majik.Core.Cards.Types.CardType.Creature);
                // Type rider: target became illegal → effect does nothing
                // (CR 608.2b — the target spell remains on the stack).
                if (requireCreature && !isCreature) return;
                if (requireNonCreature && isCreature) return;
                // Pay rider: if the controller has the generic mana available,
                // they auto-pay and the spell resolves. Otherwise → counter.
                if (unlessPayN > 0
                    && spell.Controller is not null
                    && spell.Controller.PayMana(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(unlessPayN)))
                {
                    return;
                }
                OracleSpellBinder.RemoveFromStack(stack, spell);
                spell.Card.SetZone(ZoneType.Graveyard);
            }) };
        });
}
