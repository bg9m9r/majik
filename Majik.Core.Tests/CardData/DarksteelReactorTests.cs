using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Darksteel Reactor (Darksteel, {4}).
///
/// Covers:
///   - Card identity (Artifact, name, mana cost, owner/controller) +
///     <see cref="NamedCardFactory"/> dispatch shape.
///   - Printed Indestructible keyword.
///   - Upkeep trigger adds a charge counter when fired.
///   - Reaching 20 charge counters marks every opponent as lost
///     (the engine's "win the game" surrogate — see
///     <see cref="DarksteelReactorFactory"/> xmldoc).
///   - Below 20 charges: opponents not marked.
/// </summary>
public class DarksteelReactorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _charlie = new("Charlie", 20);

    [Fact]
    public void DarksteelReactor_Identity_ArtifactAt4()
    {
        var reactor = DarksteelReactorFactory.Create(_alice);

        reactor.Name.Should().Be("Darksteel Reactor");
        reactor.ManaCost.Should().Be("{4}");
        reactor.HasType(CardType.Artifact).Should().BeTrue();
        reactor.Owner.Should().BeSameAs(_alice);
        reactor.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DarksteelReactor_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Darksteel Reactor", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Darksteel Reactor");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void DarksteelReactor_HasPrintedIndestructibleKeyword()
    {
        var reactor = DarksteelReactorFactory.Create(_alice);

        reactor.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void DarksteelReactor_HasUpkeepTrigger()
    {
        var reactor = DarksteelReactorFactory.Create(_alice);

        var upkeep = reactor.Abilities
            .OfType<TriggeredAbility>()
            .FirstOrDefault(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);

        upkeep.Should().NotBeNull();
    }

    [Fact]
    public void UpkeepTrigger_FirstTick_AddsOneChargeCounter()
    {
        var reactor = DarksteelReactorFactory.Create(_alice);
        PutOnBattlefield(reactor);

        var upkeep = reactor.Abilities
            .OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);

        foreach (var e in upkeep.Effects) e.Execute();

        reactor.Counters.Count(CounterType.Charge).Should().Be(1);
    }

    [Fact]
    public void UpkeepTrigger_MultipleTicks_AccumulatesCharges()
    {
        var reactor = DarksteelReactorFactory.Create(_alice);
        PutOnBattlefield(reactor);

        var upkeep = reactor.Abilities
            .OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);

        for (var i = 0; i < 5; i++)
        {
            foreach (var e in upkeep.Effects) e.Execute();
        }

        reactor.Counters.Count(CounterType.Charge).Should().Be(5);
    }

    [Fact]
    public void UpkeepTrigger_Below20Charges_OpponentsNotMarkedLost()
    {
        var opponents = new[] { _bob };
        var reactor = DarksteelReactorFactory.Create(_alice, eventBus: null, triggers: null, opponents: opponents);
        PutOnBattlefield(reactor);

        // Pre-seed 18 charges so the next upkeep tick reaches 19 (still
        // below threshold).
        reactor.Counters.Add(CounterType.Charge, 18);

        var upkeep = reactor.Abilities
            .OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);
        foreach (var e in upkeep.Effects) e.Execute();

        reactor.Counters.Count(CounterType.Charge).Should().Be(19);
        _bob.HasLost.Should().BeFalse();
    }

    [Fact]
    public void UpkeepTrigger_ReachesTwentyCharges_AllOpponentsMarkedLost()
    {
        var opponents = new[] { _bob, _charlie };
        var reactor = DarksteelReactorFactory.Create(_alice, eventBus: null, triggers: null, opponents: opponents);
        PutOnBattlefield(reactor);

        // Pre-seed 19 charges so the next upkeep tick crosses the
        // threshold to 20.
        reactor.Counters.Add(CounterType.Charge, 19);

        var upkeep = reactor.Abilities
            .OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);
        foreach (var e in upkeep.Effects) e.Execute();

        reactor.Counters.Count(CounterType.Charge).Should().Be(20);
        _bob.HasLost.Should().BeTrue();
        _charlie.HasLost.Should().BeTrue();
        // Controller is not marked lost.
        _alice.HasLost.Should().BeFalse();
    }

    [Fact]
    public void UpkeepTrigger_Above20Charges_StillMarksOpponentsLost()
    {
        var opponents = new[] { _bob };
        var reactor = DarksteelReactorFactory.Create(_alice, eventBus: null, triggers: null, opponents: opponents);
        PutOnBattlefield(reactor);

        // Pre-seed 24 charges (already past threshold). The next upkeep
        // tick should both bump and re-mark opponents lost (idempotent).
        reactor.Counters.Add(CounterType.Charge, 24);

        var upkeep = reactor.Abilities
            .OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);
        foreach (var e in upkeep.Effects) e.Execute();

        reactor.Counters.Count(CounterType.Charge).Should().Be(25);
        _bob.HasLost.Should().BeTrue();
    }

    [Fact]
    public void UpkeepTrigger_NoOpponentsList_TriggerStillRunsAndAddsCounter()
    {
        // Shape path — opponents=null. Trigger should still bump the
        // counter; the win-check just has nobody to mark.
        var reactor = DarksteelReactorFactory.Create(_alice);
        PutOnBattlefield(reactor);
        reactor.Counters.Add(CounterType.Charge, 19);

        var upkeep = reactor.Abilities
            .OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Events.StepStartedEvent>);
        foreach (var e in upkeep.Effects) e.Execute();

        reactor.Counters.Count(CounterType.Charge).Should().Be(20);
        _bob.HasLost.Should().BeFalse();
    }

    [Fact]
    public void DarksteelReactor_WinThreshold_Is20()
    {
        DarksteelReactorFactory.WinThreshold.Should().Be(20);
    }

    private void PutOnBattlefield(Artifact reactor)
    {
        _alice.Zones.Battlefield.AddCard(reactor);
        reactor.SetZone(ZoneType.Battlefield);
    }
}
