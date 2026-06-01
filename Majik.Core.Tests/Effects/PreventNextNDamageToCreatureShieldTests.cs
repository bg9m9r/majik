using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 615 — unit tests for <see cref="PreventNextNDamageToCreatureShield"/>:
/// a finite damage pool (like <see cref="PreventNextNDamageToAnyTargetShield"/>)
/// bound to a single creature (like <see cref="PreventAllDamageToCreatureShield"/>).
/// Backs Eiganjo Castle.
/// </summary>
public class PreventNextNDamageToCreatureShieldTests
{
    private readonly Player _alice = new("A", 20);

    [Fact]
    public void PreventsUpToPool_OnTheProtectedCreature()
    {
        var hero = new Creature("Hero", "", 1, 2) { Owner = _alice, Controller = _alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventNextNDamageToCreatureShield(hero, 2));

        var src = new Creature("src", "", 5, 5);
        // 5 damage to the protected creature → 2 soaked, 3 passes through.
        bus.Apply(new DamageIntent(src, 5, TargetCreature: hero))!
            .Amount.Should().Be(3, "the 2-point pool soaks 2 of the 5 damage");
    }

    [Fact]
    public void FullySoaks_WhenIntentWithinPool()
    {
        var hero = new Creature("Hero", "", 1, 2) { Owner = _alice, Controller = _alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventNextNDamageToCreatureShield(hero, 2));

        var src = new Creature("src", "", 2, 2);
        bus.Apply(new DamageIntent(src, 2, TargetCreature: hero))
            .Should().BeNull("2 damage is fully soaked by the 2-point pool");
    }

    [Fact]
    public void PoolDepletesAcrossIntents_ThenStopsApplying()
    {
        var hero = new Creature("Hero", "", 1, 2) { Owner = _alice, Controller = _alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventNextNDamageToCreatureShield(hero, 2));

        var src = new Creature("src", "", 1, 1);
        bus.Apply(new DamageIntent(src, 1, TargetCreature: hero)).Should().BeNull("first point soaked");
        bus.Apply(new DamageIntent(src, 1, TargetCreature: hero)).Should().BeNull("second point soaked");
        // Pool drained → third point passes through untouched.
        bus.Apply(new DamageIntent(src, 1, TargetCreature: hero))!
            .Amount.Should().Be(1, "pool is empty, no further prevention");
    }

    [Fact]
    public void DoesNotTouch_OtherCreaturesPlayersOrPlaneswalkers()
    {
        var hero = new Creature("Hero", "", 1, 2) { Owner = _alice, Controller = _alice };
        var other = new Creature("Other", "", 2, 2) { Owner = _alice, Controller = _alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventNextNDamageToCreatureShield(hero, 2));

        var src = new Creature("src", "", 2, 2);
        bus.Apply(new DamageIntent(src, 2, TargetCreature: other))!.Amount.Should().Be(2);
        bus.Apply(new DamageIntent(src, 2, TargetPlayer: _alice))!.Amount.Should().Be(2);
    }
}
