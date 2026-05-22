using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Effects;

public class PreventNextNDamageToAnyTargetShieldTests
{
    [Fact]
    public void ReducesIncomingDamageAndDrainsPool()
    {
        var bus = new ReplacementBus();
        var shield = new PreventNextNDamageToAnyTargetShield(3);
        bus.Register<DamageIntent>(shield);

        var attacker = new Creature("a", "", 5, 5);
        var defender = new Player("D", 20);

        // 5 damage intent: 3 absorbed, 2 passes through.
        var result = bus.Apply(new DamageIntent(attacker, 5, TargetPlayer: defender));

        result.Should().NotBeNull();
        result!.Amount.Should().Be(2);
        shield.RemainingPool.Should().Be(0);
    }

    [Fact]
    public void FullyAbsorbsAndCancelsWhenPoolCoversIntent()
    {
        var bus = new ReplacementBus();
        var shield = new PreventNextNDamageToAnyTargetShield(5);
        bus.Register<DamageIntent>(shield);

        var attacker = new Creature("a", "", 3, 3);
        var defender = new Player("D", 20);

        bus.Apply(new DamageIntent(attacker, 3, TargetPlayer: defender))
            .Should().BeNull("shield fully absorbs the 3 damage");
        shield.RemainingPool.Should().Be(2);
    }

    [Fact]
    public void ExpiresAfterPoolDrained_NextIntentPassesThrough()
    {
        var bus = new ReplacementBus();
        var shield = new PreventNextNDamageToAnyTargetShield(2);
        bus.Register<DamageIntent>(shield);

        var attacker = new Creature("a", "", 2, 2);
        var defender = new Player("D", 20);

        bus.Apply(new DamageIntent(attacker, 2, TargetPlayer: defender))
            .Should().BeNull();
        shield.RemainingPool.Should().Be(0);

        // Next damage intent: pool is empty so Applies should be false.
        var followUp = bus.Apply(new DamageIntent(attacker, 4, TargetPlayer: defender));
        followUp.Should().NotBeNull();
        followUp!.Amount.Should().Be(4);
    }

    [Fact]
    public void WorksAcrossMultipleIntentsUntilDrained()
    {
        var bus = new ReplacementBus();
        var shield = new PreventNextNDamageToAnyTargetShield(5);
        bus.Register<DamageIntent>(shield);

        var attacker = new Creature("a", "", 2, 2);
        var defender = new Player("D", 20);

        // First intent: 2 absorbed; pool now 3.
        bus.Apply(new DamageIntent(attacker, 2, TargetPlayer: defender))
            .Should().BeNull();
        shield.RemainingPool.Should().Be(3);

        // Second intent: 3 absorbed; pool 0; the +1 passes through.
        var second = bus.Apply(new DamageIntent(attacker, 4, TargetPlayer: defender));
        second.Should().NotBeNull();
        second!.Amount.Should().Be(1);
        shield.RemainingPool.Should().Be(0);
    }

    [Fact]
    public void DropsAtEndOfTurnEvenWithRemainingPool()
    {
        var bus = new ReplacementBus();
        var shield = new PreventNextNDamageToAnyTargetShield(10);
        bus.Register<DamageIntent>(shield);

        bus.ExpireEndOfTurn();

        // After cleanup, the shield is gone: damage passes through unchanged.
        var attacker = new Creature("a", "", 4, 4);
        var defender = new Player("D", 20);
        var result = bus.Apply(new DamageIntent(attacker, 4, TargetPlayer: defender));
        result.Should().NotBeNull();
        result!.Amount.Should().Be(4);
    }
}
