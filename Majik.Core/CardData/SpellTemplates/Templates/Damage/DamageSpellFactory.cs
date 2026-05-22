using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

internal static class DamageSpellFactory
{
    /// <summary>
    /// Push a spell-source damage intent through the ReplacementBus (when
    /// available) before committing it. Returns the final amount; 0 means
    /// the intent was cancelled or zeroed and the caller should skip the
    /// state mutation entirely.
    ///
    /// Source for the intent is the casting <see cref="Player"/> — direct
    /// damage spells don't carry an ICard reference into the effect closure
    /// today. The PreventAllCombatDamageShield filters on Creature source so
    /// Fog stays combat-only; redirection / mass prevention effects can hook
    /// here once they exist. Fully card-aware source threading is a
    /// follow-up.
    /// </summary>
    private static int Filter(ReplacementBus? bus, object source, object target, int n)
    {
        if (bus == null) return n;
        var intent = target switch
        {
            Creature c => new DamageIntent(source, n, TargetCreature: c),
            Planeswalker pw => new DamageIntent(source, n, TargetPlaneswalker: pw),
            Player pl => new DamageIntent(source, n, TargetPlayer: pl),
            _ => null,
        };
        if (intent == null) return n;
        var replaced = bus.Apply(intent);
        return replaced?.Amount ?? 0;
    }

    internal static SpellDefinition DamageAnySpell(int n, Func<object, object> resolver) =>
        DamageAnySpell(n, resolver, replacements: null, caster: null);

    internal static SpellDefinition DamageAnySpell(
        int n, Func<object, object> resolver,
        ReplacementBus? replacements, Player? caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n}", () =>
            {
                var amount = Filter(replacements, (object?)caster ?? target, target, n);
                if (amount <= 0) return;
                OracleSpellBinder.DealDamage(target, amount);
            }) };
        });

    internal static SpellDefinition DamagePlayerSpell(int n, Func<object, object> resolver) =>
        DamagePlayerSpell(n, resolver, replacements: null, caster: null);

    internal static SpellDefinition DamagePlayerSpell(
        int n, Func<object, object> resolver,
        ReplacementBus? replacements, Player? caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n} to player", () =>
            {
                if (target is not Player player) return;
                var amount = Filter(replacements, (object?)caster ?? target, player, n);
                if (amount > 0) player.LoseLife(amount);
            }) };
        });

    internal static SpellDefinition DealsXDamageEachCreatureSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: true,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p =>
        {
            var x = p.X ?? 0;
            return new IEffect[] { new Effect($"deal X={x} to each creature", () =>
            {
                var seen = new HashSet<Creature>();
                foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
                {
                    if (seen.Add(c)) c.TakeDamage(x);
                }
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
        EffectFactory: p => new IEffect[] { new Effect($"each opp loses {n}", () =>
        {
            // CR 109 — iterate every non-caster player from the chosen params'
            // AllPlayers snapshot (plumbed in via SpellCastFlow). Without it
            // (legacy callers that build ChosenSpellParams manually) the
            // effect is a no-op rather than throwing — preserves prior
            // behaviour while the production cast path now does the right
            // thing. Used by Boltwave, Pyrohemia, etc.
            var allPlayers = p.AllPlayers;
            if (allPlayers == null) return;
            foreach (var pl in allPlayers)
            {
                if (ReferenceEquals(pl, caster)) continue;
                pl.LoseLife(n);
            }
        }) });

    internal static SpellDefinition DamageCreatureSpell(int n, Func<object, object> resolver) =>
        DamageCreatureSpell(n, resolver, replacements: null, caster: null);

    internal static SpellDefinition DamageCreatureSpell(
        int n, Func<object, object> resolver,
        ReplacementBus? replacements, Player? caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n} to creature", () =>
            {
                if (target is not Creature creature) return;
                var amount = Filter(replacements, (object?)caster ?? creature, creature, n);
                if (amount > 0) creature.TakeDamage(amount);
            }) };
        });

    internal static SpellDefinition DealsXAnyTargetSpell(Func<object, object> resolver) =>
        DealsXAnyTargetSpell(resolver, replacements: null, caster: null);

    internal static SpellDefinition DealsXAnyTargetSpell(
        Func<object, object> resolver,
        ReplacementBus? replacements, Player? caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: true,
        TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            var x = p.X ?? 0;
            return new IEffect[] { new Effect($"deal X={x}", () =>
            {
                var amount = Filter(replacements, (object?)caster ?? target, target, x);
                if (amount > 0) OracleSpellBinder.DealDamage(target, amount);
            }) };
        });

    internal static SpellDefinition DealsXCreatureSpell(Func<object, object> resolver) =>
        DealsXCreatureSpell(resolver, replacements: null, caster: null);

    internal static SpellDefinition DealsXCreatureSpell(
        Func<object, object> resolver,
        ReplacementBus? replacements, Player? caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: true,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            var x = p.X ?? 0;
            return new IEffect[] { new Effect($"deal X={x} to creature", () =>
            {
                if (target is not Creature creature) return;
                var amount = Filter(replacements, (object?)caster ?? creature, creature, x);
                if (amount > 0) creature.TakeDamage(amount);
            }) };
        });
}
