using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TwinSilkSpiderFactory"/>.
///
/// Twin-Silk Spider (Bloomburrow, {2}{G}). Creature — Spider 1/2.
/// Oracle (verified against Scryfall):
///   "Reach
///    When this creature enters, create a 1/2 green Spider creature token
///    with reach."
///
/// Coverage:
/// - Identity (name, type, Spider subtype, cost, colour, P/T, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Reach keyword marker (CR 702.17).
/// - One ETB <see cref="TriggeredAbility"/> over a CardMovedEvent to the
///   battlefield, gated to this card.
/// - <see cref="TwinSilkSpiderFactory.CreateSpiderToken"/> builds a 1/2
///   green Spider token with reach on the battlefield.
/// - ETB-effect execution mints one such token under the controller.
/// </summary>
[Trait("Color", "G")]
public class TwinSilkSpiderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void TwinSilkSpider_Identity()
    {
        var c = TwinSilkSpiderFactory.Create(_alice);

        c.Name.Should().Be("Twin-Silk Spider");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{G}");
        c.ManaCostValue.TotalValue.Should().Be(3);
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── Reach ───────────────────────────────────────────────────────────

    [Fact]
    public void TwinSilkSpider_HasReach()
    {
        var c = TwinSilkSpiderFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Reach")
            .Should().HaveCount(1, "CR 702.17 — Reach is attached as a keyword marker.");
        CombatAbilities.HasReach(c).Should().BeTrue(
            "Twin-Silk Spider prints Reach (CR 702.17).");
    }

    // ── ETB trigger — structural ────────────────────────────────────────

    [Fact]
    public void TwinSilkSpider_HasOneEtbTrigger()
    {
        var card = TwinSilkSpiderFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the ETB Spider-token trigger is attached.");
        triggers[0].Source.Should().BeSameAs(card);
        triggers[0].Controller.Should().BeSameAs(_alice);
        triggers[0].Condition.Should().BeOfType<EventTriggerCondition<CardMovedEvent>>();
    }

    [Fact]
    public void EtbTrigger_Matches_OnlyThisCardEnteringBattlefield()
    {
        var card = TwinSilkSpiderFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        var cond = (EventTriggerCondition<CardMovedEvent>)trigger.Condition;

        cond.Matches(
            new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeTrue("this card entering the battlefield triggers the ability.");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        cond.Matches(
            new CardMovedEvent(other, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeFalse("another creature entering does not trigger this ability.");

        cond.Matches(
            new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard), trigger)
            .Should().BeFalse("leaving the battlefield does not trigger the ETB.");
    }

    // ── Spider token shape ──────────────────────────────────────────────

    [Fact]
    public void CreateSpiderToken_Builds_1_2_Green_Spider_With_Reach()
    {
        var token = TwinSilkSpiderFactory.CreateSpiderToken(_alice);

        token.Name.Should().Be("Spider");
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(2);
        token.IsToken.Should().BeTrue();
        token.HasType(CardType.Creature).Should().BeTrue();
        token.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        token.Owner.Should().BeSameAs(_alice);
        token.Controller.Should().BeSameAs(_alice);
        token.Zone.Should().Be(ZoneType.Battlefield,
            "the Spider token enters the battlefield directly (CR 111.6).");
        CombatAbilities.HasReach(token).Should().BeTrue(
            "the token itself has reach (CR 702.17).");
        CardColors.GetColors(token).Should().Contain(ManaColor.Green,
            "the token is green (CR 105.2a).");
    }

    // ── ETB effect — execute and observe the token landing ──────────────

    [Fact]
    public void TwinSilkSpider_EtbEffect_CreatesSpiderUnderController()
    {
        var spider = TwinSilkSpiderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spider);
        spider.SetZone(ZoneType.Battlefield);

        var trigger = spider.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var tokensOnBoard = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Spider" && c.IsToken)
            .ToList();

        tokensOnBoard.Should().HaveCount(1, "the ETB effect creates one Spider token.");
        tokensOnBoard[0].Power.Should().Be(1);
        tokensOnBoard[0].Toughness.Should().Be(2);
        tokensOnBoard[0].HasSubtype(CardSubtype.Spider).Should().BeTrue();
        CombatAbilities.HasReach(tokensOnBoard[0]).Should().BeTrue();
        CardColors.GetColors(tokensOnBoard[0]).Should().Contain(ManaColor.Green);
    }
}
