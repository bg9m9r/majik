using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Bloodchief Ascension (Zendikar, Enchantment {1}{B}).
///   "At the beginning of each opponent's end step, if an opponent lost
///    2 or more life this turn, you may put a quest counter on Bloodchief
///    Ascension."
///   "As long as Bloodchief Ascension has three or more quest counters
///    on it, whenever a card is put into an opponent's graveyard from
///    anywhere, you may have that opponent lose 2 life. If you do, you
///    gain 2 life."
///
/// Validates:
///   * Card identity (Enchantment at {1}{B}) + dispatcher entry.
///   * End-step trigger only fires on opponents' end steps AND only
///     when an opponent lost 2+ life this turn.
///   * Resolution places one Quest counter.
///   * Drain trigger is gated by 3+ Quest counters and fires when a
///     card enters an opponent's graveyard.
/// </summary>
[Trait("Color", "B")]
public class BloodchiefAscensionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BloodchiefAscension_IsEnchantment_AtCost1B()
    {
        var card = BloodchiefAscensionFactory.Create(_alice);

        card.Name.Should().Be("Bloodchief Ascension");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BloodchiefAscension_OpponentEndStep_WithLifeLoss_PlacesQuestCounter()
    {
        var card = BloodchiefAscensionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Bob has lost 3 life this turn (>= 2 threshold).
        _bob.LoseLife(3);
        _bob.LifeLostThisTurn.Should().Be(3);

        var questTrigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new StepStartedEvent(PhaseStateType.End, _bob)));

        foreach (var effect in questTrigger.Effects) effect.Execute();

        card.Counters.Count(CounterType.Quest).Should().Be(1,
            "the end-step trigger places one Quest counter when an " +
            "opponent has lost 2+ life this turn (CR 121)");
    }

    [Fact]
    public void BloodchiefAscension_OpponentEndStep_BelowLifeLossThreshold_DoesNotTrigger()
    {
        var card = BloodchiefAscensionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        _bob.LoseLife(1); // below the 2 threshold

        var trigger = card.Abilities.OfType<TriggeredAbility>().First();
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _bob)).Should().BeFalse(
            "intervening-if fails: opponent lost less than 2 life this turn (CR 603.4)");
        card.Counters.Count(CounterType.Quest).Should().Be(0);
    }

    [Fact]
    public void BloodchiefAscension_OwnEndStep_DoesNotTrigger()
    {
        var card = BloodchiefAscensionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        _bob.LoseLife(5);

        var trigger = card.Abilities.OfType<TriggeredAbility>().First();
        // Controller's own end step — printed "each opponent's end
        // step" excludes it (CR 500.4).
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice)).Should().BeFalse(
            "Ascension's quest trigger fires only on opponents' end steps");
    }

    [Fact]
    public void BloodchiefAscension_AtThreeQuestCounters_OpponentCardToGraveyard_Drains()
    {
        var card = BloodchiefAscensionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.Counters.Add(CounterType.Quest, 3);

        // A card enters Bob's graveyard.
        var milled = new Artifact("Lib0", "{0}");
        milled.SetOwner(_bob);
        milled.SetController(_bob);
        _bob.Zones.Graveyard.AddCard(milled);
        milled.SetZone(ZoneType.Graveyard);

        var drainTrigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CardMovedEvent(milled, ZoneType.Library, ZoneType.Graveyard)));

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        foreach (var effect in drainTrigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore - 2,
            "opponent loses 2 life when the threshold-gated drain trigger resolves");
        _alice.LifeTotal.Should().Be(aliceLifeBefore + 2,
            "controller gains 2 life when the drain resolves (paired payoff CR 117.6)");
    }

    [Fact]
    public void BloodchiefAscension_BelowThreshold_OpponentGraveyardCard_DoesNotTrigger()
    {
        var card = BloodchiefAscensionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.Counters.Add(CounterType.Quest, 2); // below 3

        var milled = new Artifact("Lib0", "{0}");
        milled.SetOwner(_bob);
        milled.SetController(_bob);

        var drainTrigger = card.Abilities.OfType<TriggeredAbility>()
            .First(t => t != card.Abilities.OfType<TriggeredAbility>().First());
        // The drain trigger should not fire at 2 quest counters.
        var ev = new CardMovedEvent(milled, ZoneType.Library, ZoneType.Graveyard);
        card.Abilities.OfType<TriggeredAbility>()
            .Any(t => t.IsTriggered(ev))
            .Should().BeFalse("static gate (CR 603.6e) blocks the drain at 2 counters");
    }

    [Fact]
    public void BloodchiefAscension_AtThreshold_ControllerOwnCardToGraveyard_DoesNotTrigger()
    {
        var card = BloodchiefAscensionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.Counters.Add(CounterType.Quest, 3);

        // Card owned by Alice (the controller) entering Alice's graveyard
        // — printed "an opponent's graveyard" excludes own graveyards.
        var dying = new Artifact("Mine", "{0}");
        dying.SetOwner(_alice);
        dying.SetController(_alice);

        var ev = new CardMovedEvent(dying, ZoneType.Battlefield, ZoneType.Graveyard);
        card.Abilities.OfType<TriggeredAbility>()
            .Any(t => t.IsTriggered(ev))
            .Should().BeFalse(
                "controller's own graveyard does not fire the drain (printed 'an opponent's graveyard')");
    }
}
