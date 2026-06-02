using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Mindcrank (Mirrodin Besieged, Artifact {2}).
///   "Whenever an opponent loses life, that player puts that many cards
///    from the top of their library into their graveyard."
///
/// Validates:
///   * Card identity (Artifact at {2}) + dispatcher entry.
///   * Life-loss → mill trigger fires only for opponents AND only on
///     strictly-negative life deltas.
///   * Resolution mills |delta| cards from the opponent's library into
///     their graveyard (CR 701.13).
/// </summary>
[Trait("Color", "C")]
public class MindcrankFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Mindcrank_IsArtifact_AtCost2()
    {
        var card = MindcrankFactory.Create(_alice);

        card.Name.Should().Be("Mindcrank");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Mindcrank_OpponentLosesLife_MillsThatManyCards()
    {
        var card = MindcrankFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Seed Bob's library with 5 distinguishable cards.
        for (var i = 0; i < 5; i++)
        {
            var c = new Artifact($"Lib{i}", "{0}");
            c.SetOwner(_bob);
            c.SetController(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // Simulate "Bob lost 3 life" by feeding the LifeChangedEvent
        // through the predicate (so the closure slot stamps) then
        // resolving the effect body.
        var ev = new LifeChangedEvent(_bob, previousLife: 20, newLife: 17);
        trigger.IsTriggered(ev).Should().BeTrue(
            "Mindcrank's trigger fires when an opponent loses life (CR 119.3)");

        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3,
            "Bob mills 3 cards (CR 701.13) — equal to the life lost");
        _bob.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Mindcrank_LifeGain_DoesNotTrigger()
    {
        var card = MindcrankFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // Bob GAINS 3 life — strictly-positive delta. No trigger.
        trigger.IsTriggered(new LifeChangedEvent(_bob, 20, 23)).Should().BeFalse(
            "life gain does not trigger Mindcrank (filter on negative delta only)");
    }

    [Fact]
    public void Mindcrank_ControllerLosesLife_DoesNotTrigger()
    {
        var card = MindcrankFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // Alice (the controller) loses life — NOT "an opponent loses
        // life," so the trigger should not fire.
        trigger.IsTriggered(new LifeChangedEvent(_alice, 20, 17)).Should().BeFalse(
            "Mindcrank's trigger gates on opponent, not controller (printed 'an opponent')");
    }

    [Fact]
    public void Mindcrank_ZeroLifeDelta_DoesNotTrigger()
    {
        var card = MindcrankFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new LifeChangedEvent(_bob, 20, 20)).Should().BeFalse(
            "no life-total change → not a life loss → no trigger");
    }

    [Fact]
    public void Mindcrank_OpponentLosesMoreLifeThanLibrarySize_MillsRemaining()
    {
        var card = MindcrankFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Bob's library has only 2 cards; he loses 5 life → mills all 2,
        // does NOT mill the difference into nothing. CR 701.13.
        for (var i = 0; i < 2; i++)
        {
            var c = new Artifact($"Lib{i}", "{0}");
            c.SetOwner(_bob);
            c.SetController(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new LifeChangedEvent(_bob, 20, 15)).Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
        _bob.Zones.Library.GetCards().Should().BeEmpty();
    }
}
