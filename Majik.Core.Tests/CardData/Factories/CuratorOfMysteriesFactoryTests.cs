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
/// Unit tests for <see cref="CuratorOfMysteriesFactory"/> (Amonkhet).
///
/// Covers:
/// - Identity ({2}{U}{U} Creature — Sphinx 4/4).
/// - Flying keyword marker.
/// - "Whenever you cycle ... another card" trigger shape — subscribes
///   to <see cref="CardCycledEvent"/>, gated to controller + non-self.
/// - Cycling self does NOT fire the trigger ("another card" gate).
/// - Cycle event from another card triggers a scry 1 — the top of
///   library moves to the bottom under the default-no-agent fallback.
/// - Cycling activated ability shape ({U} mana + DiscardSelfCost) and
///   end-to-end publish.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class CuratorOfMysteriesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CuratorOfMysteries_Identity_Sphinx44()
    {
        var card = CuratorOfMysteriesFactory.Create(_alice);

        card.Name.Should().Be("Curator of Mysteries");
        card.ManaCost.ToString().Should().Be("{2}{U}{U}");
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(4);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Sphinx).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CuratorOfMysteries_HasFlyingKeyword()
    {
        var card = CuratorOfMysteriesFactory.Create(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying");
    }

    [Fact]
    public void CuratorOfMysteries_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Curator of Mysteries", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSubtype(CardSubtype.Sphinx).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the cycle-or-discard scry trigger");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the cycling activated ability");
    }

    // -----------------------------------------------------------------------
    // Cycle trigger shape — CR 603.1 over CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void CuratorOfMysteries_TriggerSubscribesToCardCycledEvent()
    {
        var card = CuratorOfMysteriesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Should().BeOfType<EventTriggerCondition<CardCycledEvent>>();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "Curator's trigger functions only from the battlefield");
        trigger.TargetRequests.Should().BeEmpty("scry 1 has no targets");
    }

    // -----------------------------------------------------------------------
    // "Another card" gate — cycling self does NOT fire the scry
    // -----------------------------------------------------------------------

    [Fact]
    public void CuratorOfMysteries_TriggerCondition_DoesNotFire_OnSelfCycle()
    {
        var card = CuratorOfMysteriesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var selfEvent = new CardCycledEvent(card, _alice);
        trigger.Condition.Matches(selfEvent, trigger).Should().BeFalse(
            "Curator cycling itself does NOT trigger — 'another card' gate");
    }

    [Fact]
    public void CuratorOfMysteries_TriggerCondition_DoesNotFire_OnOpponentCycle()
    {
        var card = CuratorOfMysteriesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var otherCard = new Card("Some Cycler", "");
        var opponentEvent = new CardCycledEvent(otherCard, _bob);
        trigger.Condition.Matches(opponentEvent, trigger).Should().BeFalse(
            "Bob cycling does NOT trigger Curator — 'you cycle' gate");
    }

    [Fact]
    public void CuratorOfMysteries_TriggerCondition_Fires_OnControllerCyclingAnother()
    {
        var card = CuratorOfMysteriesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var otherCard = new Card("Some Cycler", "");
        var aliceCyclesOther = new CardCycledEvent(otherCard, _alice);
        trigger.Condition.Matches(aliceCyclesOther, trigger).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Scry-1 resolution — default-to-bottom posture moves the top card
    // -----------------------------------------------------------------------

    [Fact]
    public void CuratorOfMysteries_Resolve_ScrysOne_TopToBottomByDefault()
    {
        var topCard = new Card("Top", "");
        var second = new Card("Second", "");
        var third = new Card("Third", "");
        foreach (var c in new[] { topCard, second, third })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var card = CuratorOfMysteriesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in trigger.Effects) effect.Execute();

        // Default scry posture sends to bottom — top of library is now Second.
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(second);
        _alice.Zones.Library.GetCards().Last().Should().BeSameAs(topCard);
    }

    // -----------------------------------------------------------------------
    // Cycling activated ability — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void CuratorOfMysteries_HasCyclingActivatedAbility_WithBlueAndDiscardSelf()
    {
        var card = CuratorOfMysteriesFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2);
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Blue.Should().Be(1, "cycling {U} charges one blue");
    }

    [Fact]
    public void CuratorOfMysteries_Cycling_EndToEnd_PublishesCardCycledEvent()
    {
        var topCard = new Instant("Counterspell", "{U}{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var curator = CuratorOfMysteriesFactory.Create(_alice, triggers: null, eventBus: bus);
        _alice.Zones.Hand.AddCard(curator);
        curator.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("U"));

        var cycling = curator.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs) cost.Pay(_alice);
        foreach (var effect in cycling.Effects) effect.Execute();

        curator.Zone.Should().Be(ZoneType.Graveyard);
        captured.Should().NotBeNull();
        captured!.Card.Should().BeSameAs(curator);
    }
}
