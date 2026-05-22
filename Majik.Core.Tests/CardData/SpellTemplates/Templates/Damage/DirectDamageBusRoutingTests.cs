using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

public class DirectDamageBusRoutingTests
{
    private static SpellBindContext Ctx(string oracle, Player caster, ReplacementBus bus)
        => new(new CardEntity { Name = "X", OracleText = oracle },
            caster, _ => _, Effects: null, Stack: null, Replacements: bus);

    [Fact]
    public void DamageCreatureTemplate_RoutesThroughReplacementBus()
    {
        var caster = new Player("Caster", 20);
        var bus = new ReplacementBus();

        // Register a redirect-style effect that zeros damage to creatures.
        var fired = false;
        bus.Register(new LambdaReplacement<DamageIntent>(
            applies: (i, _) => i.TargetCreature is not null,
            replace: (i, _) => { fired = true; return i with { Amount = 0 }; }));

        var spell = new DamageCreatureTemplate().TryBind(Ctx(
            "It deals 3 damage to target creature.", caster, bus));
        spell.Should().NotBeNull();

        var target = new Creature("victim", "", 4, 4);
        var ctxResolver = (object o) => (object)target;

        // Synthesize ChosenSpellParams targeting the creature.
        var p = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        // Replay with a real resolver pointing to target.
        spell = DamageSpellFactory.DamageCreatureSpell(3, _ => target, bus, caster);
        foreach (var fx in spell.EffectFactory(p)) fx.Execute();

        fired.Should().BeTrue("bus saw the intent");
        target.Damage.Should().Be(0, "the redirect zeroed the amount before TakeDamage");
    }

    [Fact]
    public void DamagePlayerTemplate_RoutesThroughBus_CancelDropsLifeLoss()
    {
        var caster = new Player("Caster", 20);
        var victim = new Player("Victim", 20);
        var bus = new ReplacementBus();

        // Full cancellation — the intent vanishes.
        bus.Register(new LambdaReplacement<DamageIntent>(
            applies: (i, _) => i.TargetPlayer is not null,
            replace: (i, _) => null));

        var spell = DamageSpellFactory.DamagePlayerSpell(5, _ => victim, bus, caster);
        var p = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { victim } },
            Mana: ManaPayment.Empty);
        foreach (var fx in spell.EffectFactory(p)) fx.Execute();

        victim.LifeTotal.Should().Be(20, "cancelled intent applies no damage");
    }

    [Fact]
    public void DamageCreatureSpell_NoBus_BypassesReplacementCheck()
    {
        // Backwards-compat: the no-bus overload still works for tests + the
        // many call sites that haven't been threaded through yet.
        var target = new Creature("victim", "", 4, 4);
        var spell = DamageSpellFactory.DamageCreatureSpell(2, _ => target);
        var p = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var fx in spell.EffectFactory(p)) fx.Execute();

        target.Damage.Should().Be(2);
    }
}
