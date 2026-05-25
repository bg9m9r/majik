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

public class SubtypeEntersTappedBinderTests
{
    [Fact]
    public void Bind_MatchesSpymastersVaultSwampClause()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Spymaster's Vault");
        land.SetOwner(alice);
        var entity = new CardEntity
        {
            Name = "Spymaster's Vault",
            OracleText = "Spymaster's Vault enters tapped unless you control a Swamp. {T}: Add {B}.",
            TypeLine = "Land",
        };

        SubtypeEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue();
    }

    [Fact]
    public void Bind_MatchesCheckLandTwoSubtypeClause()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Drowned Catacomb");
        land.SetOwner(alice);
        var entity = new CardEntity
        {
            Name = "Drowned Catacomb",
            OracleText = "Drowned Catacomb enters tapped unless you control an Island or a Swamp.",
            TypeLine = "Land",
        };

        SubtypeEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue();
    }

    [Theory]
    [InlineData("Boseiju, Who Endures enters tapped unless you control two or fewer other lands.")] // Count, not subtype
    [InlineData("Sacred Foundry — As Sacred Foundry enters, you may pay 2 life. If you don't, it enters tapped.")] // Shock
    [InlineData("Mountain")]
    [InlineData("")]
    public void Bind_SkipsCountVariantsAndUnrelatedText(string oracleText)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Test Land");
        land.SetOwner(alice);
        var entity = new CardEntity { Name = "Test Land", OracleText = oracleText, TypeLine = "Land" };

        SubtypeEntersTappedBinder.Bind(land, entity, bus).Should().BeFalse();
    }

    [Fact]
    public void Bind_ReturnsFalse_WhenSubtypeWordIsUnknown()
    {
        // "Squirrel" is in CardSubtype enum so use a contrived non-subtype.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Test Land");
        land.SetOwner(alice);
        var entity = new CardEntity
        {
            Name = "Test Land",
            OracleText = "Test Land enters tapped unless you control a Garbanzo.",
            TypeLine = "Land",
        };

        SubtypeEntersTappedBinder.Bind(land, entity, bus).Should().BeFalse();
    }

    [Fact]
    public void Single_LandEntersUntapped_WhenControllerHasMatchingSubtype()
    {
        var (zones, alice, land) = SetupSwampGated();

        var swamp = new Land("Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        swamp.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(swamp);
        swamp.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Single_LandEntersTapped_WhenControllerDoesNotHaveMatchingSubtype()
    {
        var (zones, alice, land) = SetupSwampGated();

        // Controller has only a Forest — no Swamp.
        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TwoSubtype_LandEntersUntapped_WhenControllerHasEitherSubtype()
    {
        var (zones, alice, land) = SetupCheckLand();

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        island.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(island);
        island.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void TwoSubtype_LandEntersTapped_WhenControllerHasNeitherSubtype()
    {
        var (zones, alice, land) = SetupCheckLand();

        var mountain = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        mountain.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Self_DoesNotCountTowardSubtypePresence()
    {
        // A self-typed Swamp ETB land entering as the sole permanent must NOT
        // satisfy "control a Swamp" via itself. Hypothetical "Swampy Vault" —
        // the binder excludes self.
        var bus = new ReplacementBus();
        var eventBus = new EventBus();
        var zones = new ZoneService(eventBus, bus);
        var alice = new Player("Alice", 20);
        var land = new Land("Swampy Vault",
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var entity = new CardEntity
        {
            Name = "Swampy Vault",
            OracleText = "Swampy Vault enters tapped unless you control a Swamp.",
            TypeLine = "Land — Swamp",
        };
        SubtypeEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue();

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeTrue();
    }

    private static (ZoneService zones, Player alice, Land land) SetupSwampGated()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);
        var alice = new Player("Alice", 20);
        var land = new Land("Spymaster's Vault");
        land.SetOwner(alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);
        var entity = new CardEntity
        {
            Name = "Spymaster's Vault",
            OracleText = "Spymaster's Vault enters tapped unless you control a Swamp.",
            TypeLine = "Land",
        };
        SubtypeEntersTappedBinder.Bind(land, entity, rep).Should().BeTrue();
        return (zones, alice, land);
    }

    private static (ZoneService zones, Player alice, Land land) SetupCheckLand()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);
        var alice = new Player("Alice", 20);
        var land = new Land("Drowned Catacomb");
        land.SetOwner(alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);
        var entity = new CardEntity
        {
            Name = "Drowned Catacomb",
            OracleText = "Drowned Catacomb enters tapped unless you control an Island or a Swamp.",
            TypeLine = "Land",
        };
        SubtypeEntersTappedBinder.Bind(land, entity, rep).Should().BeTrue();
        return (zones, alice, land);
    }
}
