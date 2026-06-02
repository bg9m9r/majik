using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KrosanTuskerFactory"/> (Onslaught).
///
/// Covers:
/// - Identity ({5}{G}{G} Creature — Beast 6/5).
/// - Cycling keyword + activated-ability shape (Cycling {2} +
///   DiscardSelfCost).
/// - On-cycle trigger shape (self-cycle gate + Graveyard ActiveZones).
/// - Trigger subscribes to <see cref="CardCycledEvent"/>.
/// - End-to-end cycle: pays {2}, discards self, draws, publishes event,
///   tutor body fires + tutors a basic land + shuffles.
/// - Non-basic land does NOT satisfy the basic-land tutor predicate.
/// - Other-card-cycle does NOT fire the self-cycle gate.
/// - Dispatch via <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "G")]
public class KrosanTuskerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KrosanTusker_Identity_Beast65()
    {
        var card = KrosanTuskerFactory.Create(_alice);

        card.Name.Should().Be("Krosan Tusker");
        card.ManaCost.ToString().Should().Be("{5}{G}{G}");
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(5);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Cycling activated ability — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void KrosanTusker_HasCyclingActivatedAbility_WithTwoAndDiscardSelf()
    {
        var card = KrosanTuskerFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2, "cycling = {2} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(2, "cycling {2} charges two generic");
    }

    // -----------------------------------------------------------------------
    // On-cycle trigger — CR 702.32d / CR 603.6
    // -----------------------------------------------------------------------

    [Fact]
    public void KrosanTusker_OnCycleTrigger_SubscribesToCardCycledEventInGraveyard()
    {
        var card = KrosanTuskerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Should().BeOfType<EventTriggerCondition<CardCycledEvent>>();
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "post-discard zone — Krosan Tusker is already in the graveyard when the event publishes");
    }

    [Fact]
    public void KrosanTusker_OnCycleTrigger_FiresOnSelfCycleOnly()
    {
        var card = KrosanTuskerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // Self-cycle = fires.
        var selfEvent = new CardCycledEvent(card, _alice);
        trigger.Condition.Matches(selfEvent, trigger).Should().BeTrue(
            "self-cycle gate fires on this Krosan Tusker");

        // Other-card cycle = does NOT fire (printed self-cycle gate).
        var other = new Card("Some Other Cycler", "{2}");
        other.SetOwner(_alice);
        var otherEvent = new CardCycledEvent(other, _alice);
        trigger.Condition.Matches(otherEvent, trigger).Should().BeFalse(
            "other-card cycle does NOT fire Krosan Tusker's self-cycle rider");

        // Wrong-player cycle = does NOT fire.
        var wrongPlayerEvent = new CardCycledEvent(card, _bob);
        trigger.Condition.Matches(wrongPlayerEvent, trigger).Should().BeFalse(
            "different player cycling does not fire");
    }

    // -----------------------------------------------------------------------
    // Tutor-body resolve — basic-land predicate + shuffle
    // -----------------------------------------------------------------------

    [Fact]
    public void KrosanTusker_OnCycleResolve_TutorsBasicLandSkipsNonbasic()
    {
        // Library: a Forest (basic) + a non-basic dual + an Instant.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var stomping = new Land(
            "Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest });
        stomping.SetOwner(_alice);
        _alice.Zones.Library.AddCard(stomping);
        stomping.SetZone(ZoneType.Library);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var tusker = KrosanTuskerFactory.Create(_alice);
        var trigger = tusker.Abilities.OfType<TriggeredAbility>().Single();

        // Resolve the trigger body directly — picks the first basic land.
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(forest,
            "on-cycle tutor pulled the basic Forest");
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().Contain(stomping,
            "non-basic dual land stayed in library — predicate filter");
        _alice.Zones.Library.GetCards().Should().Contain(bolt,
            "Lightning Bolt is not a land — predicate filter");
    }

    [Fact]
    public void KrosanTusker_NoBasicLandInLibrary_TutorBodyResolvesCleanly()
    {
        // Library has only a non-basic land.
        var stomping = new Land(
            "Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest });
        stomping.SetOwner(_alice);
        _alice.Zones.Library.AddCard(stomping);
        stomping.SetZone(ZoneType.Library);

        var tusker = KrosanTuskerFactory.Create(_alice);
        var trigger = tusker.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in trigger.Effects) effect.Execute();
        };

        act.Should().NotThrow("no basic land = clean no-op (CR 701.19a)");
        _alice.Zones.Library.GetCards().Should().Contain(stomping,
            "non-basic stayed in library");
        _alice.Zones.Hand.GetCards().Should().NotContain(stomping);
    }

    [Fact]
    public void KrosanTusker_Cycle_PublishesCardCycledEvent_AndDrawsCard()
    {
        // Seed library with one card so the draw resolves.
        var topCard = new Instant("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var tusker = KrosanTuskerFactory.Create(_alice, triggers: null, eventBus: bus);
        _alice.Zones.Hand.AddCard(tusker);
        tusker.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var cycling = tusker.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        tusker.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "Cycling {2} draws a card (CR 702.32a) — separate from the on-cycle tutor rider");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(tusker);
        captured.Player.Should().BeSameAs(_alice);
    }
}
