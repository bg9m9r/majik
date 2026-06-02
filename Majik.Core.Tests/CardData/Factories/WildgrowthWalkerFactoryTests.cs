using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WildgrowthWalkerFactory"/> (Guilds of Ravnica,
/// {1}{G}). Creature — Elemental 1/3.
///   "Whenever a creature you control explores, put a +1/+1 counter on this
///    creature and you gain 3 life."
/// </summary>
[Trait("Color", "G")]
public class WildgrowthWalkerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
        EventBusRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WildgrowthWalker_Identity()
    {
        var c = WildgrowthWalkerFactory.Create(_alice);

        c.Name.Should().Be("Wildgrowth Walker");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WildgrowthWalker_HasExactlyOneTriggeredAbility()
    {
        var c = WildgrowthWalkerFactory.Create(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Trigger condition — "a creature YOU control" (CR 109.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void WildgrowthWalker_Trigger_MatchesOwnControllersExplore()
    {
        var walker = WildgrowthWalkerFactory.Create(_alice);
        walker.SetZone(ZoneType.Battlefield); // trigger is battlefield-active
        var trigger = walker.Abilities.OfType<TriggeredAbility>().Single();

        var explorer = new Creature("Explorer", "{G}", 1, 1);
        var ev = new CreatureExploredEvent(explorer, _alice, revealedCard: null, revealedLand: false);

        trigger.IsTriggered(ev).Should().BeTrue(
            "Alice's creature explored — Wildgrowth Walker (controlled by Alice) triggers");
    }

    [Fact]
    public void WildgrowthWalker_Trigger_DoesNotMatchOpponentExplore()
    {
        var walker = WildgrowthWalkerFactory.Create(_alice);
        walker.SetZone(ZoneType.Battlefield);
        var trigger = walker.Abilities.OfType<TriggeredAbility>().Single();

        var explorer = new Creature("Explorer", "{G}", 1, 1);
        var ev = new CreatureExploredEvent(explorer, _bob, revealedCard: null, revealedLand: false);

        trigger.IsTriggered(ev).Should().BeFalse(
            "CR 109.5 — only a creature YOU control exploring triggers it");
    }

    // -----------------------------------------------------------------------
    // Effect — +1/+1 counter on Wildgrowth Walker + 3 life
    // -----------------------------------------------------------------------

    [Fact]
    public void WildgrowthWalker_Effect_PlacesCounter_And_Gains3Life()
    {
        var walker = WildgrowthWalkerFactory.Create(_alice);
        var trigger = walker.Abilities.OfType<TriggeredAbility>().Single();

        var lifeBefore = _alice.LifeTotal;
        foreach (var effect in trigger.Effects) effect.Execute();

        walker.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the payoff puts a +1/+1 counter on Wildgrowth Walker itself");
        _alice.LifeTotal.Should().Be(lifeBefore + WildgrowthWalkerFactory.LifeGain,
            "CR 119.3 — the controller gains 3 life");
    }

    // -----------------------------------------------------------------------
    // End-to-end via the bus: a registered trigger fires off a published
    // CreatureExploredEvent.
    // -----------------------------------------------------------------------

    [Fact]
    public void WildgrowthWalker_RegisteredTrigger_FiresOnPublishedExploreEvent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var walker = WildgrowthWalkerFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(walker);
        walker.SetZone(ZoneType.Battlefield);

        var explorer = new Creature("Explorer", "{G}", 1, 1);
        var ev = new CreatureExploredEvent(explorer, _alice, revealedCard: null, revealedLand: false);

        triggers.EvaluateTriggers(ev);

        triggers.PendingCount.Should().Be(1,
            "a creature Alice controls explored — Wildgrowth Walker's payoff is triggered");
    }
}
