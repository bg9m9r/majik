using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BallLightningFactory"/> (Mirage, {R}{R}{R}).
///
/// Creature — Elemental 6/1. Oracle text:
///   "Trample
///    Haste
///    At the beginning of the end step, sacrifice this creature."
///
/// Covers:
///   - Identity (Elemental 6/1 at {R}{R}{R}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch (JSON-backed base shape).
///   - Trample (CR 702.19) + Haste (CR 702.10) keyword markers.
///   - The printed end-step self-sacrifice <see cref="TriggeredAbility"/>
///     is attached structurally on the shape-only path.
///   - On the beginning of the next end step the creature is sacrificed
///     (battlefield → its owner's graveyard).
///   - The trigger fires on ANY player's end step (the printed clause has
///     no possessive), so an opponent's end step also sacrifices it.
/// </summary>
[Trait("Color", "R")]
public class BallLightningFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BallLightning_Identity()
    {
        var c = BallLightningFactory.Create(_alice);

        c.Name.Should().Be("Ball Lightning");
        c.ManaCost.Should().Be("{R}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BallLightning_HasTrampleAndHaste()
    {
        var c = BallLightningFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();

        keywords.Should().Contain("Trample", "CR 702.19");
        keywords.Should().Contain("Haste", "CR 702.10");
    }

    [Fact]
    public void BallLightning_HasOneEndStepTriggeredAbility()
    {
        var c = BallLightningFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the printed end-step self-sacrifice trigger");
    }

    [Fact]
    public void EndStepTrigger_OnOwnEndStep_CreatureIsSacrificed()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = BallLightningFactory.Create(_alice, triggers, zones);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // "At the beginning of the end step, sacrifice this creature."
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // CR 701.16 — sacrificed to its owner's graveyard.
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }

    [Fact]
    public void EndStepTrigger_FiresOnAnyPlayersEndStep()
    {
        var card = BallLightningFactory.Create(_alice, triggers: null, zoneService: null);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // The printed clause has no possessive ("the end step"), so the
        // condition matches an End step started by ANY player — CR 603.3a.
        trigger.Condition
            .Matches(new StepStartedEvent(PhaseStateType.End, _bob), trigger)
            .Should().BeTrue("Ball Lightning sacrifices itself on the next end step regardless of whose turn it is");

        // It does not fire on a non-End step.
        trigger.Condition
            .Matches(new StepStartedEvent(PhaseStateType.Upkeep, _alice), trigger)
            .Should().BeFalse();
    }
}
