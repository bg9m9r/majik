using FluentAssertions;
using Majik.Core.Effects;
using Xunit;

namespace Majik.Core.Tests.Effects;

public class ReplacementBusTests
{
    public sealed record DamageIntent(int Amount, string Target);

    [Fact]
    public void Apply_NoEffects_ReturnsInputUnchanged()
    {
        var bus = new ReplacementBus();
        var intent = new DamageIntent(3, "Bob");

        var result = bus.Apply(intent);

        result.Should().Be(intent);
    }

    [Fact]
    public void Apply_SingleReplacement_Transforms()
    {
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<DamageIntent>(
            (i, _) => i.Target == "Bob",
            (i, _) => i with { Amount = i.Amount - 1 }));

        bus.Apply(new DamageIntent(3, "Bob")).Amount.Should().Be(2);
        bus.Apply(new DamageIntent(3, "Alice")).Amount.Should().Be(3);
    }

    [Fact]
    public void Apply_Cancellation_ReturnsNull()
    {
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<DamageIntent>(
            (_, _) => true,
            (_, _) => null));

        bus.Apply(new DamageIntent(3, "Bob")).Should().BeNull();
    }

    [Fact]
    public void Apply_ChainedReplacements_StopWhenOneAppliesThenReEvaluates()
    {
        var bus = new ReplacementBus();
        // First: halve damage
        bus.Register(new LambdaReplacement<DamageIntent>(
            (_, history) => !history.Any(h => (string)h == "Halve"),
            (i, _) => i with { Amount = i.Amount / 2 },
            tag: "Halve"));
        // Second: add 1
        bus.Register(new LambdaReplacement<DamageIntent>(
            (_, history) => !history.Any(h => (string)h == "Plus1"),
            (i, _) => i with { Amount = i.Amount + 1 },
            tag: "Plus1"));

        // Both apply once each (in some order); result depends on order chosen.
        var result = bus.Apply(new DamageIntent(10, "X"));

        // Either: (10/2)+1=6 or (10+1)/2=5; framework uses registration order.
        result!.Amount.Should().BeOneOf(5, 6);
    }

    [Fact]
    public void OneShot_Unregistered_AfterFiring()
    {
        var bus = new ReplacementBus();
        var oneShot = new LambdaReplacement<DamageIntent>(
            (_, _) => true,
            (i, _) => i with { Amount = 0 },
            oneShot: true);
        bus.Register(oneShot);

        bus.Apply(new DamageIntent(5, "X"))!.Amount.Should().Be(0);
        bus.Apply(new DamageIntent(5, "X"))!.Amount.Should().Be(5);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var bus = new ReplacementBus();
        var act = () => bus.Register<DamageIntent>(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
