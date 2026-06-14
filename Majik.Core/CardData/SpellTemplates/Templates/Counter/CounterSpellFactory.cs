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
                // CR 701.5b — uncounterable spells survive the attempt.
                if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
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
                // CR 701.5b — uncounterable spells survive the attempt.
                if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
                spell.Card.SetZone(ZoneType.Graveyard);
            }) };
        });

    /// <summary>
    /// "Counter target [type] spell unless its controller pays {N}." —
    /// Spell Pierce / Mana Tithe / Mana Leak shape. The pay rider is wired
    /// through <see cref="Majik.Core.Primitives.PayUnlessCounterRider"/>: at
    /// resolution the target spell's CONTROLLER is asked (CR 118.4) whether to
    /// pay {N} to keep their spell on the stack; on "yes" + affordable it is
    /// spent and the counter no-ops, on "no" / can't afford the spell is
    /// countered (CR 701.5). The legacy synchronous (shape-only) path keeps the
    /// deterministic "pay if able" posture. <paramref name="requireCreature"/>
    /// and <paramref name="requireNonCreature"/> let the same factory cover
    /// typed variants (Spell Pierce — noncreature) alongside the plain
    /// "target spell" form; an illegal type at resolution is a clean no-op
    /// (CR 608.2b).
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
            var raw = p.Targets[0][0];
            // The type rider is applied inside the resolveTarget closure: a
            // target whose type no longer matches the predicate resolves to
            // null, so PayUnlessCounterRider does nothing (CR 608.2b).
            return new IEffect[]
            {
                Majik.Core.Primitives.PayUnlessCounterRider.Build(
                    "counter target spell unless its controller pays",
                    stack,
                    () =>
                    {
                        if (resolver(raw) is not ISpell spell) return null;
                        var isCreature = spell.Card.HasType(Majik.Core.Cards.Types.CardType.Creature);
                        if (requireCreature && !isCreature) return null;
                        if (requireNonCreature && isCreature) return null;
                        return spell;
                    },
                    unlessPayN: unlessPayN),
            };
        });
}
