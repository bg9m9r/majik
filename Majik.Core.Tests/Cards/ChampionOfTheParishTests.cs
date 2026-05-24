using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Unit tests for <see cref="ChampionOfTheParishFactory"/> and
/// <see cref="ThaliaLieutenantFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, P/T, Human + Soldier subtypes,
///   owner/controller) for both cards.
/// - NamedCardFactory dispatch for both.
/// - Champion: another Human enters → Champion gains +1/+1 counter.
/// - Champion: a non-Human creature enters → no counter.
/// - Thalia's Lieutenant ETB-self: two other Humans on battlefield →
///   both get +1/+1 counters.
/// - Thalia's Lieutenant ETB-other: another Human enters while
///   Lieutenant is in play → Lieutenant gains +1/+1 counter.
/// - Cross-card: Champion and Lieutenant each trigger each other when
///   the other enters (both are Humans).
/// - Condition check: trigger does not fire for opponent's Humans.
/// </summary>
public class ChampionOfTheParishTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // ChampionOfTheParish — Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ChampionOfTheParish_Identity()
    {
        var champion = ChampionOfTheParishFactory.Create(_alice);

        champion.Name.Should().Be("Champion of the Parish");
        champion.ManaCost.Should().Be("{W}");
        champion.HasType(CardType.Creature).Should().BeTrue();
        champion.HasSubtype(CardSubtype.Human).Should().BeTrue(
            "Champion of the Parish is a Human");
        champion.HasSubtype(CardSubtype.Soldier).Should().BeTrue(
            "Champion of the Parish is a Soldier");
        champion.BasePower.Should().Be(1);
        champion.BaseToughness.Should().Be(1);
        champion.Owner.Should().BeSameAs(_alice);
        champion.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ChampionOfTheParish_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Champion of the Parish", _alice);

        card.Should().BeOfType<Creature>("Champion of the Parish is a Creature");
        card.Name.Should().Be("Champion of the Parish");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB-other-Human trigger is attached");
    }

    // -----------------------------------------------------------------------
    // ChampionOfTheParish — Trigger behavior
    // -----------------------------------------------------------------------

    [Fact]
    public void ChampionOfTheParish_AnotherHumanEnters_GetsCounter()
    {
        var champion = ChampionOfTheParishFactory.Create(_alice);
        champion.SetZone(ZoneType.Battlefield);

        // Another Human enters under Alice's control.
        var human = new Creature("Elite Vanguard", "{W}", 2, 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });
        human.SetOwner(_alice);
        human.SetController(_alice);

        var trigger = champion.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            card: human,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        // Condition should match — another Human entered under controller.
        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Champion's trigger fires when another Human enters under controller");

        // Execute the effect: Champion gains a +1/+1 counter.
        foreach (var effect in trigger.Effects) effect.Execute();

        champion.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Champion gains one +1/+1 counter when a Human ETBs");
    }

    [Fact]
    public void ChampionOfTheParish_NonHumanCreatureEnters_NoCounter()
    {
        var champion = ChampionOfTheParishFactory.Create(_alice);
        champion.SetZone(ZoneType.Battlefield);

        // A non-Human creature enters.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = champion.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            card: bear,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Champion's trigger does NOT fire when a non-Human enters");

        champion.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no counter placed when no Human entered");
    }

    [Fact]
    public void ChampionOfTheParish_OpponentHumanEnters_NoCounter()
    {
        var champion = ChampionOfTheParishFactory.Create(_alice);
        champion.SetZone(ZoneType.Battlefield);

        // Bob (opponent) controls a Human.
        var oppHuman = new Creature("Thalia, Guardian of Thraben", "{1}{W}", 2, 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });
        oppHuman.SetOwner(_bob);
        oppHuman.SetController(_bob);

        var trigger = champion.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            card: oppHuman,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Champion's trigger does NOT fire for an opponent's Human");

        champion.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no counter placed for opponent's Human ETB");
    }

    [Fact]
    public void ChampionOfTheParish_SelfEnters_DoesNotTrigger()
    {
        var champion = ChampionOfTheParishFactory.Create(_alice);
        champion.SetZone(ZoneType.Hand);

        var trigger = champion.Abilities.OfType<TriggeredAbility>().Single();
        // Champion itself enters (e.g. from hand to battlefield).
        var moveEvent = new CardMovedEvent(
            card: champion,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Champion's trigger is for ANOTHER Human, not itself");
    }

    // -----------------------------------------------------------------------
    // ThaliaLieutenant — Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliaLieutenant_Identity()
    {
        var lieutenant = ThaliaLieutenantFactory.Create(_alice);

        lieutenant.Name.Should().Be("Thalia's Lieutenant");
        lieutenant.ManaCost.Should().Be("{1}{W}");
        lieutenant.HasType(CardType.Creature).Should().BeTrue();
        lieutenant.HasSubtype(CardSubtype.Human).Should().BeTrue(
            "Thalia's Lieutenant is a Human");
        lieutenant.HasSubtype(CardSubtype.Soldier).Should().BeTrue(
            "Thalia's Lieutenant is a Soldier");
        lieutenant.BasePower.Should().Be(1);
        lieutenant.BaseToughness.Should().Be(1);
        lieutenant.Owner.Should().BeSameAs(_alice);
        lieutenant.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThaliaLieutenant_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Thalia's Lieutenant", _alice);

        card.Should().BeOfType<Creature>("Thalia's Lieutenant is a Creature");
        card.Name.Should().Be("Thalia's Lieutenant");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one ETB-self trigger and one ETB-other-Human trigger");
    }

    // -----------------------------------------------------------------------
    // ThaliaLieutenant — ETB-self: pump all other Humans
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliaLieutenant_EtbSelf_TwoOtherHumans_BothGetCounters()
    {
        var lieutenant = ThaliaLieutenantFactory.Create(_alice);
        lieutenant.SetZone(ZoneType.Battlefield);

        // Two other Humans already on the battlefield.
        var human1 = new Creature("Champion of the Parish", "{W}", 1, 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });
        human1.SetOwner(_alice);
        human1.SetController(_alice);
        human1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(human1);

        var human2 = new Creature("Thalia, Guardian of Thraben", "{1}{W}", 2, 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });
        human2.SetOwner(_alice);
        human2.SetController(_alice);
        human2.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(human2);

        // Execute the ETB-self trigger (first triggered ability).
        var etbSelfTrigger = lieutenant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etbSelfTrigger.Effects) effect.Execute();

        human1.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "first Human gets a +1/+1 counter from Thalia's Lieutenant ETB");
        human2.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "second Human gets a +1/+1 counter from Thalia's Lieutenant ETB");
        lieutenant.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Thalia's Lieutenant itself is excluded from its own ETB pump");
    }

    [Fact]
    public void ThaliaLieutenant_EtbSelf_NonHumanOnBattlefield_NotPumped()
    {
        var lieutenant = ThaliaLieutenantFactory.Create(_alice);
        lieutenant.SetZone(ZoneType.Battlefield);

        // A non-Human on the battlefield.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var etbSelfTrigger = lieutenant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etbSelfTrigger.Effects) effect.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-Human creature is NOT pumped by Thalia's Lieutenant ETB");
    }

    [Fact]
    public void ThaliaLieutenant_EtbSelf_NoBoardPrior_NoCounters()
    {
        var lieutenant = ThaliaLieutenantFactory.Create(_alice);
        lieutenant.SetZone(ZoneType.Battlefield);

        // No other Humans — ETB effect should be a clean no-op.
        var etbSelfTrigger = lieutenant.Abilities.OfType<TriggeredAbility>().First();

        var act = () => { foreach (var effect in etbSelfTrigger.Effects) effect.Execute(); };

        act.Should().NotThrow("ETB-self with no other Humans is a safe no-op");
        lieutenant.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no counter placed on Lieutenant when no other Humans are in play");
    }

    // -----------------------------------------------------------------------
    // ThaliaLieutenant — ETB-other-Human trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void ThaliaLieutenant_AnotherHumanEnters_LieutenantGetsCounter()
    {
        var lieutenant = ThaliaLieutenantFactory.Create(_alice);
        lieutenant.SetZone(ZoneType.Battlefield);

        var human = new Creature("Champion of the Parish", "{W}", 1, 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });
        human.SetOwner(_alice);
        human.SetController(_alice);

        // Second TriggeredAbility is the ETB-other-Human trigger.
        var humanEtbTrigger = lieutenant.Abilities.OfType<TriggeredAbility>().Skip(1).First();
        var moveEvent = new CardMovedEvent(
            card: human,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        humanEtbTrigger.Condition.Matches(moveEvent, humanEtbTrigger).Should().BeTrue(
            "Lieutenant's second trigger fires when another Human enters");

        foreach (var effect in humanEtbTrigger.Effects) effect.Execute();

        lieutenant.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Thalia's Lieutenant gains a +1/+1 counter when another Human enters");
    }

    [Fact]
    public void ThaliaLieutenant_OpponentHumanEnters_NoCounter()
    {
        var lieutenant = ThaliaLieutenantFactory.Create(_alice);
        lieutenant.SetZone(ZoneType.Battlefield);

        var oppHuman = new Creature("Soldier", "{W}", 1, 1,
            subtypes: new[] { CardSubtype.Human });
        oppHuman.SetOwner(_bob);
        oppHuman.SetController(_bob);

        var humanEtbTrigger = lieutenant.Abilities.OfType<TriggeredAbility>().Skip(1).First();
        var moveEvent = new CardMovedEvent(
            card: oppHuman,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        humanEtbTrigger.Condition.Matches(moveEvent, humanEtbTrigger).Should().BeFalse(
            "Lieutenant's trigger does NOT fire for an opponent's Human");
    }

    // -----------------------------------------------------------------------
    // Cross-card interaction: Champion and Lieutenant trigger each other
    // -----------------------------------------------------------------------

    [Fact]
    public void CrossCard_LieutenantEnters_ChampionGetsCounter()
    {
        // Champion is already on the battlefield.
        var champion = ChampionOfTheParishFactory.Create(_alice);
        champion.SetZone(ZoneType.Battlefield);

        // Thalia's Lieutenant (a Human) enters.
        var lieutenant = ThaliaLieutenantFactory.Create(_alice);

        var championTrigger = champion.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            card: lieutenant,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        // Thalia's Lieutenant is a Human, so Champion's trigger fires.
        championTrigger.Condition.Matches(moveEvent, championTrigger).Should().BeTrue(
            "Champion fires when Thalia's Lieutenant (a Human) enters");

        foreach (var effect in championTrigger.Effects) effect.Execute();

        champion.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Champion gets a +1/+1 counter when Thalia's Lieutenant enters");
    }

    [Fact]
    public void CrossCard_ChampionEnters_LieutenantGetsCounter()
    {
        // Thalia's Lieutenant is already on the battlefield.
        var lieutenant = ThaliaLieutenantFactory.Create(_alice);
        lieutenant.SetZone(ZoneType.Battlefield);

        // Champion of the Parish (a Human) enters.
        var champion = ChampionOfTheParishFactory.Create(_alice);

        var lieutenantHumanTrigger = lieutenant.Abilities.OfType<TriggeredAbility>().Skip(1).First();
        var moveEvent = new CardMovedEvent(
            card: champion,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        lieutenantHumanTrigger.Condition.Matches(moveEvent, lieutenantHumanTrigger).Should().BeTrue(
            "Lieutenant fires when Champion of the Parish (a Human) enters");

        foreach (var effect in lieutenantHumanTrigger.Effects) effect.Execute();

        lieutenant.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Thalia's Lieutenant gets a +1/+1 counter when Champion of the Parish enters");
    }
}
