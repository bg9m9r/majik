using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Stack;
using Majik.Core.ValueObjects;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Services;

/// <summary>
/// Unit tests for AbilityActivator service.
/// Tests ability activation, cost payment, and event publishing.
/// </summary>
public class AbilityActivatorTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly AbilityActivator _abilityActivator;

    public AbilityActivatorTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _stack = new Majik.Core.Stack.Stack(_eventBusMock.Object);
        _abilityActivator = new AbilityActivator(_stack, _eventBusMock.Object);
    }

    [Fact]
    public void CanActivate_ValidAbility_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Staff of Fire", "") { Owner = player, Controller = player };
        var ability = new ActivatedAbility(source, player);

        // Act
        var result = _abilityActivator.CanActivate(ability, player);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanActivate_NullAbility_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        var result = _abilityActivator.CanActivate(null!, player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanActivate_NullPlayer_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Staff of Fire", "") { Owner = player };
        var ability = new ActivatedAbility(source, player);

        // Act
        var result = _abilityActivator.CanActivate(ability, null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ActivateAbility_ValidAbility_AddsToStack()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Staff of Fire", "") { Owner = player, Controller = player };
        var ability = new ActivatedAbility(source, player);

        // Act
        _abilityActivator.ActivateAbility(ability, player);

        // Assert
        _stack.Count.Should().Be(1);
        _stack.Top.Should().NotBeNull();
    }

    [Fact]
    public void ActivateAbility_WithCosts_PaysCosts()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));
        var source = new Artifact("Staff of Fire", "") { Owner = player, Controller = player };
        var ability = new ActivatedAbility(source, player);
        var costs = new List<ICost> { new ManaCostCost("R") };

        // Act
        _abilityActivator.ActivateAbility(ability, player, null, costs);

        // Assert
        player.ManaPool.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ActivateAbility_PublishesEvents()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Staff of Fire", "") { Owner = player, Controller = player };
        var ability = new ActivatedAbility(source, player);

        // Act
        _abilityActivator.ActivateAbility(ability, player);

        // Assert
        _eventBusMock.Verify(x => x.Publish(It.IsAny<AbilityActivatedEvent>()), Times.Once);
    }

    [Fact]
    public void ActivateAbility_WithTargets_PublishesTargetsChosenEvent()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var source = new Artifact("Staff of Fire", "") { Owner = player, Controller = player };
        var ability = new ActivatedAbility(source, player);
        var targets = new List<Targeting.ITarget> { Targeting.Target.Player(targetPlayer) };

        // Act
        _abilityActivator.ActivateAbility(ability, player, targets);

        // Assert
        _eventBusMock.Verify(x => x.Publish(It.IsAny<TargetsChosenEvent>()), Times.Once);
    }

    [Fact]
    public void ActivateAbility_NullAbility_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act & Assert
        _abilityActivator.Invoking(a => a.ActivateAbility(null!, player))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ActivateAbility_NullPlayer_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Staff of Fire", "") { Owner = player };
        var ability = new ActivatedAbility(source, player);

        // Act & Assert
        _abilityActivator.Invoking(a => a.ActivateAbility(ability, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ActivateAbility_InsufficientMana_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Staff of Fire", "") { Owner = player, Controller = player };
        var ability = new ActivatedAbility(source, player);
        var costs = new List<ICost> { new ManaCostCost("R") };
        // No mana added

        // Act & Assert
        _abilityActivator.Invoking(a => a.ActivateAbility(ability, player, null, costs))
            .Should().Throw<InvalidPlayerActionException>();
    }

    // -----------------------------------------------------------------------
    // Regression: AbilityActivator used to drop effects/targetRequests/
    // sorcerySpeed when constructing the on-stack wrapper. That made every
    // non-mana activated ability resolve to a no-op (fetchlands didn't
    // sacrifice or fetch, etc.). The wrapper must preserve those fields, or
    // the resolver pops an empty-effect stub.
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivateAbility_PushedStackObject_PreservesEffects()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Mox Diamond", "") { Owner = player, Controller = player };
        var effectA = new Effect("eff-a", () => { });
        var effectB = new Effect("eff-b", () => { });
        var ability = new ActivatedAbility(
            source: source,
            controller: player,
            effects: new IEffect[] { effectA, effectB });

        // Act
        _abilityActivator.ActivateAbility(ability, player);

        // Assert — the wrapper pushed onto the stack must still carry the effects.
        var pushed = _stack.Top.Should().BeAssignableTo<ActivatedAbility>().Subject;
        pushed.Effects.Should().HaveCount(2);
        pushed.Effects.Should().Contain(effectA);
        pushed.Effects.Should().Contain(effectB);
    }

    [Fact]
    public void ActivateAbility_PushedStackObject_PreservesTargetRequests_AndSorcerySpeed()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Pristine Talisman", "") { Owner = player, Controller = player };
        var request = new Majik.Core.Players.Agents.TargetRequest(
            Description: "any target",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>());
        var ability = new ActivatedAbility(
            source: source,
            controller: player,
            targetRequests: new[] { request },
            sorcerySpeed: true);

        // Act
        _abilityActivator.ActivateAbility(ability, player);

        // Assert
        var pushed = _stack.Top.Should().BeAssignableTo<ActivatedAbility>().Subject;
        pushed.TargetRequests.Should().HaveCount(1);
        pushed.TargetRequests[0].Should().BeSameAs(request);
        pushed.IsSorcerySpeed.Should().BeTrue();
    }

    [Fact]
    public void ResolveTop_AbilityWithEffect_InvokesEffectExecute()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var source = new Artifact("Aether Vial", "") { Owner = player, Controller = player };
        var executed = false;
        var effect = new Effect("flag", () => executed = true);
        var ability = new ActivatedAbility(
            source: source,
            controller: player,
            effects: new IEffect[] { effect });

        _abilityActivator.ActivateAbility(ability, player);

        var resolver = new StackResolver();

        // Act
        resolver.ResolveTop(_stack);

        // Assert — if the activator dropped Effects, executed stays false.
        executed.Should().BeTrue();
    }
}
