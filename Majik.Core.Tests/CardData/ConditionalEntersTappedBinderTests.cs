using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class ConditionalEntersTappedBinderTests
{
    [Fact]
    public void Bind_RegistersReplacement_ForBoseijuStyleTwoOrFewer()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Boseiju, Who Endures");
        land.SetOwner(alice);
        var entity = new CardEntity
        {
            Name = "Boseiju, Who Endures",
            OracleText = "Boseiju, Who Endures enters tapped unless you control two or fewer other lands.",
            TypeLine = "Legendary Land",
        };

        ConditionalEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue();
    }

    [Fact]
    public void Bind_RegistersReplacement_ForMortuaryStyleTwoOrMore()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Underground Mortuary");
        land.SetOwner(alice);
        var entity = new CardEntity
        {
            Name = "Underground Mortuary",
            OracleText = "Underground Mortuary enters tapped unless you control two or more other lands.",
            TypeLine = "Land",
        };

        ConditionalEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue();
    }

    [Theory]
    [InlineData("Spymaster's Vault enters tapped unless you control a Swamp.")]
    [InlineData("Drowned Catacomb enters tapped unless you control an Island or a Swamp.")]
    [InlineData("Sacred Foundry — As Sacred Foundry enters, you may pay 2 life. If you don't, it enters tapped.")]
    [InlineData("Mountain")]
    [InlineData("")]
    public void Bind_DoesNotMatchUnrelatedClauses(string oracleText)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Test Land");
        land.SetOwner(alice);
        var entity = new CardEntity { Name = "Test Land", OracleText = oracleText, TypeLine = "Land" };

        ConditionalEntersTappedBinder.Bind(land, entity, bus).Should().BeFalse();
    }

    [Fact]
    public void TwoOrMore_LandEntersUntapped_WhenControllerHasTwoOtherLands()
    {
        var (zones, alice, land) = SetupWithLand(
            "Underground Mortuary",
            "Underground Mortuary enters tapped unless you control two or more other lands.");

        // Seed two other lands already on the battlefield.
        for (int i = 0; i < 2; i++)
        {
            var swamp = new Land("Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
            swamp.SetOwner(alice);
            alice.Zones.Battlefield.AddCard(swamp);
            swamp.SetZone(ZoneType.Battlefield);
        }

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void TwoOrMore_LandEntersTapped_WhenControllerHasOnlyOneOtherLand()
    {
        var (zones, alice, land) = SetupWithLand(
            "Underground Mortuary",
            "Underground Mortuary enters tapped unless you control two or more other lands.");

        var swamp = new Land("Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        swamp.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(swamp);
        swamp.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TwoOrFewer_LandEntersUntapped_WhenControllerHasTwoOtherLands()
    {
        var (zones, alice, land) = SetupWithLand(
            "Boseiju, Who Endures",
            "Boseiju, Who Endures enters tapped unless you control two or fewer other lands.");

        for (int i = 0; i < 2; i++)
        {
            var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
            forest.SetOwner(alice);
            alice.Zones.Battlefield.AddCard(forest);
            forest.SetZone(ZoneType.Battlefield);
        }

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void TwoOrFewer_LandEntersTapped_WhenControllerHasThreeOtherLands()
    {
        var (zones, alice, land) = SetupWithLand(
            "Boseiju, Who Endures",
            "Boseiju, Who Endures enters tapped unless you control two or fewer other lands.");

        for (int i = 0; i < 3; i++)
        {
            var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
            forest.SetOwner(alice);
            alice.Zones.Battlefield.AddCard(forest);
            forest.SetZone(ZoneType.Battlefield);
        }

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Self_DoesNotCountTowardOtherLandsTotal()
    {
        // The card itself must be excluded from the "other lands" count.
        // Boseiju entering as the controller's first land: 0 other lands ≤ 2
        // → enters untapped.
        var (zones, alice, land) = SetupWithLand(
            "Boseiju, Who Endures",
            "Boseiju, Who Endures enters tapped unless you control two or fewer other lands.");

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeFalse();
    }

    private static (ZoneService zones, Player alice, Land land) SetupWithLand(string name, string oracleText)
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);
        var alice = new Player("Alice", 20);
        var land = new Land(name);
        land.SetOwner(alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);
        var entity = new CardEntity { Name = name, OracleText = oracleText, TypeLine = "Land" };
        ConditionalEntersTappedBinder.Bind(land, entity, rep).Should().BeTrue();
        return (zones, alice, land);
    }
}
