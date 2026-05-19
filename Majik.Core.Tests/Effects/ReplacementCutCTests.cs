using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class ReplacementCutCTests
{
    private readonly Player _alice = new("Alice", 20);

    // ---------- ETB-with-counters (Hardened Scales) ----------

    [Fact]
    public void HardenedScales_BumpsPlusOnePlusOneAmountByOne()
    {
        var bus = new ReplacementBus();
        var bear = new Creature("Walker", "2G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        // Replacement: if +1/+1 counters would be placed, increase amount by 1.
        bus.Register(new LambdaReplacement<CounterAddIntent>(
            applies: (i, _) => i.Type == CounterType.PlusOnePlusOne,
            replace: (i, _) => i with { Amount = i.Amount + 1 }));

        var intent = new CounterAddIntent(bear, CounterType.PlusOnePlusOne, 2);
        var final = bus.Apply(intent);
        final.Should().NotBeNull();
        final!.Amount.Should().Be(3);
    }

    [Fact]
    public void HardenedScales_DoesNotApplyToMinusOneMinusOne()
    {
        var bus = new ReplacementBus();
        var bear = new Creature("X", "2G", 2, 2) { Owner = _alice, Controller = _alice };
        bus.Register(new LambdaReplacement<CounterAddIntent>(
            applies: (i, _) => i.Type == CounterType.PlusOnePlusOne,
            replace: (i, _) => i with { Amount = i.Amount + 1 }));

        var final = bus.Apply(new CounterAddIntent(bear, CounterType.MinusOneMinusOne, 2));
        final!.Amount.Should().Be(2);
    }

    // ---------- Regeneration ----------

    [Fact]
    public void RegenerationShield_CancelsDestroy_TapsClearsDamage()
    {
        var bus = new ReplacementBus();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bear.TakeDamage(1);

        var shield = new RegenerationShieldEffect(bear);
        bus.Register(shield);

        var result = bus.Apply(new DestroyIntent(bear));
        result.Should().BeNull(); // destroy cancelled
        bear.IsTapped.Should().BeTrue();
        bear.Damage.Should().Be(0);
    }

    [Fact]
    public void RegenerationShield_IsOneShot_SecondDestroyGoesThrough()
    {
        var bus = new ReplacementBus();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bus.Register(new RegenerationShieldEffect(bear));

        var first = bus.Apply(new DestroyIntent(bear));
        first.Should().BeNull();

        var second = bus.Apply(new DestroyIntent(bear));
        second.Should().NotBeNull(); // shield consumed
    }

    [Fact]
    public void RegenerationShield_OnlyAppliesToItsTarget()
    {
        var bus = new ReplacementBus();
        var protectedBear = new Creature("A", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var otherBear = new Creature("B", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bus.Register(new RegenerationShieldEffect(protectedBear));

        var result = bus.Apply(new DestroyIntent(otherBear));
        result.Should().NotBeNull(); // shield doesn't fire
        protectedBear.IsTapped.Should().BeFalse(); // shield untouched
    }
}
