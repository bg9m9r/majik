using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Services;

public class StackResolverTriggerTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);

    public StackResolverTriggerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _resolver = new StackResolver(_bus);
    }

    [Fact]
    public void ResolveTop_TriggerWithInterveningIfFalse_CountersWithoutEffects()
    {
        var ran = false;
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source),
            effects: new IEffect[] { new Effect("eff", () => ran = true) },
            interveningIf: () => false);
        _stack.Push(ability);

        TriggeredAbilityCounteredEvent? countered = null;
        _bus.Subscribe<TriggeredAbilityCounteredEvent>(e => countered = e);

        _resolver.ResolveTop(_stack);

        ran.Should().BeFalse();
        countered.Should().NotBeNull();
        countered!.Ability.Should().BeSameAs(ability);
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ResolveTop_TriggerWithInterveningIfTrue_ResolvesEffects()
    {
        var ran = false;
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source),
            effects: new IEffect[] { new Effect("eff", () => ran = true) },
            interveningIf: () => true);
        _stack.Push(ability);

        _resolver.ResolveTop(_stack);

        ran.Should().BeTrue();
        _stack.IsEmpty.Should().BeTrue();
    }
}
