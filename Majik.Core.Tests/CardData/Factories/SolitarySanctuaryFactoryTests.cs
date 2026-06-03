using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SolitarySanctuaryFactory"/> — Enchantment {2}{W}
/// (Scryfall, verified 2026-06-02):
///   "When this enchantment enters, tap target creature an opponent controls
///    and put a stun counter on it.
///    Whenever you tap an untapped creature an opponent controls, put a
///    +1/+1 counter on target creature you control."
///
/// Closure for the tap-event-and-whenever-you-tap-trigger deferral.
/// </summary>
[Trait("Color", "W")]
public class SolitarySanctuaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature AddCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Majik.Core.Game.GameContext Ctx(params Player[] players) =>
        new(
            self: players[0],
            allPlayers: players,
            activePlayer: players[0],
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

    [Fact]
    public void SolitarySanctuary_IsEnchantment_AtCost2W()
    {
        var card = SolitarySanctuaryFactory.Create(_alice);

        card.Name.Should().Be("Solitary Sanctuary");
        card.ManaCost.Should().Be("{2}{W}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SolitarySanctuary_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Solitary Sanctuary", _alice);

        card.Should().BeOfType<Enchantment>();
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void SolitarySanctuary_HasEtbAndTapPayoffTriggers()
    {
        var card = SolitarySanctuaryFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2);
    }

    [Fact]
    public void EtbTrigger_OffersOnlyOpponentCreatures()
    {
        var card = SolitarySanctuaryFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var myCreature = AddCreature(_alice);
        var oppCreature = AddCreature(_bob);

        var etb = card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Any(r => r.Description.Contains("opponent")));
        var req = etb.TargetRequests[0];

        var candidates = req.CandidateGatherer!(Ctx(_alice, _bob));

        candidates.Should().Contain(oppCreature);
        candidates.Should().NotContain(myCreature);
    }

    [Fact]
    public void TapPayoffTrigger_OffersOnlyYourCreatures()
    {
        var card = SolitarySanctuaryFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var myCreature = AddCreature(_alice);
        var oppCreature = AddCreature(_bob);

        var payoff = card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Any(r => r.Description.Contains("you control")));
        var req = payoff.TargetRequests[0];

        var candidates = req.CandidateGatherer!(Ctx(_alice, _bob));

        candidates.Should().Contain(myCreature);
        candidates.Should().NotContain(oppCreature);
    }

    [Fact]
    public void EtbResolution_TapsTarget_AndPlacesStunCounter()
    {
        var card = SolitarySanctuaryFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var oppCreature = AddCreature(_bob);

        var etb = card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Any(r => r.Description.Contains("opponent")));
        etb.SetChosenTargets(new[] { new object[] { oppCreature } });

        foreach (var fx in etb.Effects) fx.Execute();

        oppCreature.IsTapped.Should().BeTrue("CR 701.21 — the ETB taps the target");
        oppCreature.Counters.Count(CounterType.Stun).Should().Be(1);
    }

    [Fact]
    public void TapPayoffResolution_PutsPlusOnePlusOneCounter_OnYourCreature()
    {
        var card = SolitarySanctuaryFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var myCreature = AddCreature(_alice);

        var payoff = card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Any(r => r.Description.Contains("you control")));
        payoff.SetChosenTargets(new[] { new object[] { myCreature } });

        foreach (var fx in payoff.Effects) fx.Execute();

        myCreature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void TapPayoffCondition_Fires_OnlyForControllerTappingOpponentCreature()
    {
        var card = SolitarySanctuaryFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var oppCreature = AddCreature(_bob);
        var myCreature = AddCreature(_alice);

        var payoff = card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Any(r => r.Description.Contains("you control")));

        // You tap an opponent's creature -> fires.
        payoff.Condition.Matches(
            new PermanentTappedEvent(oppCreature, causedBy: _alice), payoff)
            .Should().BeTrue();

        // You tap your own creature -> no.
        payoff.Condition.Matches(
            new PermanentTappedEvent(myCreature, causedBy: _alice), payoff)
            .Should().BeFalse();

        // The opponent taps their own creature -> no (not "you").
        payoff.Condition.Matches(
            new PermanentTappedEvent(oppCreature, causedBy: _bob), payoff)
            .Should().BeFalse();
    }
}
