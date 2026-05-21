using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="OrcishBowmastersFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, subtypes, power/toughness)
/// - Owner and controller assignment
/// - Flash keyword ability wired
/// - No triggered abilities in v1 (ETB/draw-watcher deferred)
/// </summary>
public class OrcishBowmastersTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void OrcishBowmasters_NameIsCorrect()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.Name.Should().Be("Orcish Bowmasters");
    }

    [Fact]
    public void OrcishBowmasters_IsCreature()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void OrcishBowmasters_HasCorrectSubtypes()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.HasSubtype(CardSubtype.Orc).Should().BeTrue("Orcish Bowmasters is an Orc");
        ob.HasSubtype(CardSubtype.Archer).Should().BeTrue("Orcish Bowmasters is an Archer");
    }

    [Fact]
    public void OrcishBowmasters_HasCorrectStats()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.BasePower.Should().Be(1);
        ob.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void OrcishBowmasters_OwnerAndControllerAreSet()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.Owner.Should().BeSameAs(_alice);
        ob.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Flash keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void OrcishBowmasters_HasFlashKeyword()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Flash",
                "Orcish Bowmasters has Flash");
    }

    // -----------------------------------------------------------------------
    // Deferred abilities not yet wired
    // -----------------------------------------------------------------------

    [Fact]
    public void OrcishBowmasters_HasNoTriggeredAbilities_InV1()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB damage trigger and opponent-draw watcher are deferred in v1");
    }

    [Fact]
    public void OrcishBowmasters_HasNoActivatedAbilities()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Orcish Bowmasters has no activated abilities");
    }
}
