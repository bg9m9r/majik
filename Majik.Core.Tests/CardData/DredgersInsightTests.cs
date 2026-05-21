using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DredgersInsightFactory"/>.
///
/// Covers:
/// - Card identity (name, Enchantment type, owner/controller)
/// - Two triggered abilities: ETB mill-and-pick, and lifegain-on-graveyard-leave
/// - ETB effect: mills 4, picks first artifact/creature/land from milled cards
/// - ETB effect: non-matching cards remain in graveyard
/// - ETB effect: empty library is a no-op
/// - Lifegain trigger fires on artifact/creature leaving controller's graveyard
/// - Lifegain trigger does NOT fire for non-artifact/creature cards leaving
/// - Lifegain trigger does NOT fire for opponent's graveyard
/// </summary>
public class DredgersInsightTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DredgersInsight_IsEnchantment()
    {
        var enchant = DredgersInsightFactory.Create(_alice);

        enchant.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void DredgersInsight_NameIsCorrect()
    {
        var enchant = DredgersInsightFactory.Create(_alice);

        enchant.Name.Should().Be("Dredger's Insight");
    }

    [Fact]
    public void DredgersInsight_OwnerAndControllerAreSet()
    {
        var enchant = DredgersInsightFactory.Create(_alice);

        enchant.Owner.Should().BeSameAs(_alice);
        enchant.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DredgersInsight_HasExactlyTwoTriggeredAbilities()
    {
        var enchant = DredgersInsightFactory.Create(_alice);

        enchant.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one ETB mill-and-pick trigger and one lifegain-on-graveyard-leave trigger");
    }

    [Fact]
    public void DredgersInsight_HasNoManaAbilities()
    {
        var enchant = DredgersInsightFactory.Create(_alice);

        enchant.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Dredger's Insight produces no mana");
    }

    // -----------------------------------------------------------------------
    // ETB trigger: mill 4, pick first artifact/creature/land
    // -----------------------------------------------------------------------

    [Fact]
    public void DredgersInsight_EtbEffect_MillsFourCards()
    {
        var alice = new Player("Alice", 20);
        for (var i = 0; i < 6; i++)
        {
            var c = new Card($"Card{i}", "");
            c.SetOwner(alice);
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var enchant = DredgersInsightFactory.Create(alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        // 4 milled; if none is a/c/l, all 4 stay in graveyard
        alice.Zones.Graveyard.GetCards().Should().HaveCount(4,
            "mill 4 moves exactly 4 cards to the graveyard (none are a/c/l here)");
        alice.Zones.Library.GetCards().Should().HaveCount(2,
            "2 cards remain in the library after milling 4 of 6");
    }

    [Fact]
    public void DredgersInsight_EtbEffect_PutsFirstCreatureIntoHand()
    {
        var alice = new Player("Alice", 20);
        var instant = new Instant("Shock", "R");
        instant.SetOwner(alice);
        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(alice);
        alice.Zones.Library.AddCard(instant);
        instant.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var enchant = DredgersInsightFactory.Create(alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(creature,
            "first creature milled goes to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(instant,
            "the non-creature milled card remains in graveyard");
        alice.Zones.Graveyard.GetCards().Should().NotContain(creature,
            "the picked creature is removed from the graveyard and moved to hand");
        creature.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DredgersInsight_EtbEffect_PutsFirstArtifactIntoHand()
    {
        var alice = new Player("Alice", 20);
        var artifact = new Artifact("Sol Ring", "1");
        artifact.SetOwner(alice);
        alice.Zones.Library.AddCard(artifact);
        artifact.SetZone(ZoneType.Library);

        var enchant = DredgersInsightFactory.Create(alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(artifact,
            "artifact milled from top of library goes to hand");
        artifact.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DredgersInsight_EtbEffect_PutsFirstLandIntoHand()
    {
        var alice = new Player("Alice", 20);
        var land = new Land("Forest");
        land.SetOwner(alice);
        alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);

        var enchant = DredgersInsightFactory.Create(alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(land,
            "land milled from library goes to hand");
        land.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DredgersInsight_EtbEffect_NoQualifyingCard_NothingGoesToHand()
    {
        var alice = new Player("Alice", 20);
        var instant = new Instant("Counterspell", "UU");
        instant.SetOwner(alice);
        alice.Zones.Library.AddCard(instant);
        instant.SetZone(ZoneType.Library);

        var enchant = DredgersInsightFactory.Create(alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no artifact/creature/land was milled, so nothing goes to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(instant);
    }

    [Fact]
    public void DredgersInsight_EtbEffect_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);

        var enchant = DredgersInsightFactory.Create(alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("milling an empty library is a no-op");
    }

    // -----------------------------------------------------------------------
    // Lifegain trigger condition checks
    // -----------------------------------------------------------------------

    [Fact]
    public void DredgersInsight_LifegainTrigger_FiresForCreatureLeavingOwnersGraveyard()
    {
        var alice = new Player("Alice", 20);
        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(alice);

        var enchant = DredgersInsightFactory.Create(alice);
        // The second trigger is the lifegain trigger (ETB first, lifegain second).
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            card: creature,
            fromZone: ZoneType.Graveyard,
            toZone: ZoneType.Hand);

        lifegainTrigger.Condition.Matches(moveEvent, lifegainTrigger).Should().BeTrue(
            "a creature card leaving the controller's graveyard should trigger the lifegain");
    }

    [Fact]
    public void DredgersInsight_LifegainTrigger_FiresForArtifactLeavingOwnersGraveyard()
    {
        var alice = new Player("Alice", 20);
        var artifact = new Artifact("Sol Ring", "1");
        artifact.SetOwner(alice);

        var enchant = DredgersInsightFactory.Create(alice);
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            card: artifact,
            fromZone: ZoneType.Graveyard,
            toZone: ZoneType.Exile);

        lifegainTrigger.Condition.Matches(moveEvent, lifegainTrigger).Should().BeTrue(
            "an artifact card leaving the controller's graveyard triggers the lifegain");
    }

    [Fact]
    public void DredgersInsight_LifegainTrigger_DoesNotFireForInstantLeavingGraveyard()
    {
        var alice = new Player("Alice", 20);
        var instant = new Instant("Counterspell", "UU");
        instant.SetOwner(alice);

        var enchant = DredgersInsightFactory.Create(alice);
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            card: instant,
            fromZone: ZoneType.Graveyard,
            toZone: ZoneType.Hand);

        lifegainTrigger.Condition.Matches(moveEvent, lifegainTrigger).Should().BeFalse(
            "an instant (non-artifact, non-creature) leaving graveyard should NOT trigger");
    }

    [Fact]
    public void DredgersInsight_LifegainTrigger_DoesNotFireForOpponentsGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        // Creature owned by Bob — its Owner is Bob, not alice
        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(bob);

        var enchant = DredgersInsightFactory.Create(alice);
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            card: creature,
            fromZone: ZoneType.Graveyard,
            toZone: ZoneType.Hand);

        lifegainTrigger.Condition.Matches(moveEvent, lifegainTrigger).Should().BeFalse(
            "card leaving an opponent's graveyard should NOT trigger Dredger's Insight");
    }

    [Fact]
    public void DredgersInsight_LifegainEffect_GainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var enchant = DredgersInsightFactory.Create(alice);
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        foreach (var effect in lifegainTrigger.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "lifegain trigger adds exactly 1 life");
    }
}
