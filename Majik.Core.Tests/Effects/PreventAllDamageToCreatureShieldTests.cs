using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Unit tests for <see cref="PreventAllDamageToCreatureShield"/>.
/// </summary>
public class PreventAllDamageToCreatureShieldTests
{
    [Fact]
    public void BlocksDamageToProtectedCreature()
    {
        var alice = new Player("A", 20);
        var hero = new Creature("Hero", "", 1, 2) { Owner = alice, Controller = alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToCreatureShield(hero));

        var src = new Creature("src", "", 3, 3);
        bus.Apply(new DamageIntent(src, 3, TargetCreature: hero))
            .Should().BeNull("shield prevents damage to the protected creature");
    }

    [Fact]
    public void DoesNotBlockDamageToOtherCreatures()
    {
        var alice = new Player("A", 20);
        var hero = new Creature("Hero", "", 1, 2) { Owner = alice, Controller = alice };
        var other = new Creature("Other", "", 2, 2) { Owner = alice, Controller = alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToCreatureShield(hero));

        var src = new Creature("src", "", 3, 3);
        var result = bus.Apply(new DamageIntent(src, 2, TargetCreature: other));
        result.Should().NotBeNull(
            "shield is creature-scoped and does not cover other permanents");
        result!.Amount.Should().Be(2);
    }

    [Fact]
    public void DoesNotBlockDamageToPlayersOrPlaneswalkers()
    {
        var alice = new Player("A", 20);
        var hero = new Creature("Hero", "", 1, 2) { Owner = alice, Controller = alice };
        var pw = new Planeswalker("PW", "", 4) { Owner = alice, Controller = alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToCreatureShield(hero));

        var src = new Creature("src", "", 3, 3);
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: alice))!.Amount.Should().Be(3);
        bus.Apply(new DamageIntent(src, 2, TargetPlaneswalker: pw))!.Amount.Should().Be(2);
    }

    [Fact]
    public void ZeroAmountDamageDoesNotMatch()
    {
        var alice = new Player("A", 20);
        var hero = new Creature("Hero", "", 1, 2) { Owner = alice, Controller = alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToCreatureShield(hero));

        var src = new Creature("src", "", 0, 0);
        var result = bus.Apply(new DamageIntent(src, 0, TargetCreature: hero));
        // A 0-amount intent shouldn't be cancelled; the shield's Applies
        // returns false. Returns the intent unchanged.
        result.Should().NotBeNull();
        result!.Amount.Should().Be(0);
    }

    [Fact]
    public void DropsAtEndOfTurn()
    {
        var alice = new Player("A", 20);
        var hero = new Creature("Hero", "", 1, 2) { Owner = alice, Controller = alice };
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToCreatureShield(hero));
        bus.ExpireEndOfTurn();

        var src = new Creature("src", "", 3, 3);
        var result = bus.Apply(new DamageIntent(src, 3, TargetCreature: hero));
        result.Should().NotBeNull("EOT cleanup drops the shield");
        result!.Amount.Should().Be(3);
    }
}
