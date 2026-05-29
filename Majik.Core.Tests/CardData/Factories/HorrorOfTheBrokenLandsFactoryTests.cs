using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HorrorOfTheBrokenLandsFactory"/> (Hour of
/// Devastation, {4}{B}). Creature — Horror 4/4. Oracle text (verified
/// against Scryfall):
///   "Whenever you cycle or discard another card, this creature gets
///    +2/+1 until end of turn.
///    Cycling {B} ({B}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity ({4}{B} Creature — Horror 4/4) materialised from JSON.
/// - "Whenever you cycle ... another card" trigger shape — subscribes to
///   <see cref="CardCycledEvent"/>, gated to controller + non-self,
///   battlefield-only (same posture as <see cref="CuratorOfMysteriesFactory"/>).
/// - "Another card" / "you cycle" gates (self-cycle + opponent-cycle no-op).
/// - Pump resolution: controller cycling another card pumps Horror +2/+1
///   until end of turn through the layers pipeline.
/// - Cycling activated ability shape ({B} mana + DiscardSelfCost) + end-to-end.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class HorrorOfTheBrokenLandsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HorrorOfTheBrokenLands_Identity_Horror44()
    {
        var card = HorrorOfTheBrokenLandsFactory.Create(_alice);

        card.Name.Should().Be("Horror of the Broken Lands");
        card.ManaCost.ToString().Should().Be("{4}{B}");
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(4);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HorrorOfTheBrokenLands_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Horror of the Broken Lands", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the cycle-or-discard pump trigger");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the cycling activated ability");
    }

    // -----------------------------------------------------------------------
    // Cycle trigger shape — CR 603.1 over CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void HorrorOfTheBrokenLands_TriggerSubscribesToCardCycledEvent()
    {
        var card = HorrorOfTheBrokenLandsFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Should().BeOfType<EventTriggerCondition<CardCycledEvent>>();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "Horror's trigger functions only from the battlefield");
        trigger.TargetRequests.Should().BeEmpty("the self-pump has no targets");
    }

    // -----------------------------------------------------------------------
    // "Another card" / "you cycle" gates
    // -----------------------------------------------------------------------

    [Fact]
    public void HorrorOfTheBrokenLands_TriggerCondition_DoesNotFire_OnSelfCycle()
    {
        var card = HorrorOfTheBrokenLandsFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var selfEvent = new CardCycledEvent(card, _alice);
        trigger.Condition.Matches(selfEvent, trigger).Should().BeFalse(
            "Horror cycling itself does NOT trigger — 'another card' gate");
    }

    [Fact]
    public void HorrorOfTheBrokenLands_TriggerCondition_DoesNotFire_OnOpponentCycle()
    {
        var card = HorrorOfTheBrokenLandsFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var otherCard = new Card("Some Cycler", "");
        var opponentEvent = new CardCycledEvent(otherCard, _bob);
        trigger.Condition.Matches(opponentEvent, trigger).Should().BeFalse(
            "Bob cycling does NOT trigger Horror — 'you cycle' gate");
    }

    [Fact]
    public void HorrorOfTheBrokenLands_TriggerCondition_Fires_OnControllerCyclingAnother()
    {
        var card = HorrorOfTheBrokenLandsFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var otherCard = new Card("Some Cycler", "");
        var aliceCyclesOther = new CardCycledEvent(otherCard, _alice);
        trigger.Condition.Matches(aliceCyclesOther, trigger).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Pump resolution — +2/+1 until end of turn (CR 613 Layer 7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void HorrorOfTheBrokenLands_Resolve_Pumps_Plus2Plus1_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var card = HorrorOfTheBrokenLandsFactory.Create(_alice, effects: effects, triggers: null);

        card.Power.Should().Be(4, "base power before the pump");
        card.Toughness.Should().Be(4, "base toughness before the pump");

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        card.Power.Should().Be(6, "+2 from the cycle/discard pump");
        card.Toughness.Should().Be(5, "+1 from the cycle/discard pump");
    }

    // -----------------------------------------------------------------------
    // Cycling activated ability — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void HorrorOfTheBrokenLands_HasCyclingActivatedAbility_WithBlackAndDiscardSelf()
    {
        var card = HorrorOfTheBrokenLandsFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2);
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Black.Should().Be(1, "cycling {B} charges one black");
    }

    [Fact]
    public void HorrorOfTheBrokenLands_Cycling_EndToEnd_PublishesCardCycledEvent()
    {
        var topCard = new Instant("Dark Ritual", "{B}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var horror = HorrorOfTheBrokenLandsFactory.Create(
            _alice, effects: null, triggers: null, eventBus: bus);
        _alice.Zones.Hand.AddCard(horror);
        horror.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("B"));

        var cycling = horror.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs) cost.Pay(_alice);
        foreach (var effect in cycling.Effects) effect.Execute();

        horror.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycling drew a card");
        captured.Should().NotBeNull();
        captured!.Card.Should().BeSameAs(horror);
    }
}
