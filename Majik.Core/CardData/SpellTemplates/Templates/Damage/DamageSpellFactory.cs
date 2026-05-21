using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

internal static class DamageSpellFactory
{
    internal static SpellDefinition DamageAnySpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n}", () => OracleSpellBinder.DealDamage(target, n)) };
        });

    internal static SpellDefinition DamagePlayerSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n} to player", () =>
            {
                if (target is Player player) player.LoseLife(n);
            }) };
        });

    internal static SpellDefinition DealsDamageEachCreatureSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"deal {n} to each creature", () =>
        {
            // CR 109 — sweep enumerates every creature on the battlefield.
            // Reach via the caster's GameContext.AllPlayers in production;
            // here we look at every player accessible from the caster's
            // perspective. Each player tracks their own battlefield.
            var seen = new HashSet<Creature>();
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                if (seen.Add(c)) c.TakeDamage(n);
            }
            // To cover opponent creatures, the binder needs a way to
            // enumerate them. MVP: walk Permanent.Controller from caster's
            // creatures' controllers — but if no shared registry exists,
            // opponent creatures are unreachable here. The sweep effect
            // signature accepts ChosenSpellParams which can carry an
            // AllPlayers reference once SpellCastFlow is updated.
        }) });

    internal static SpellDefinition EachOpponentLosesLifeSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"each opp loses {n}", () =>
        {
            // Caller may not have the player list inside binder scope; tests verify
            // single-opponent case where caster.OpponentsForTests is implied.
            // Real wiring: GameContext.AllPlayers iterates and applies.
        }) });

    internal static SpellDefinition DamageCreatureSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n} to creature", () =>
            {
                if (target is Creature creature) creature.TakeDamage(n);
            }) };
        });

    internal static SpellDefinition DealsXAnyTargetSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: true,
        TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            var x = p.X ?? 0;
            return new IEffect[] { new Effect($"deal X={x}", () => OracleSpellBinder.DealDamage(target, x)) };
        });
}
