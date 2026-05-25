using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Legacy shape tests for <see cref="OrcishBowmastersFactory"/>. Covers:
/// card identity (name, Creature type, subtypes, power/toughness), owner
/// + controller assignment, Flash keyword wiring, no activated abilities.
///
/// The v2 ETB + opponent-draw + Amass Orcs 1 trigger coverage lives in
/// <see cref="Majik.Core.Tests.CardData.Factories.OrcishBowmastersFactoryTests"/>.
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
    // Triggered + activated ability shape (v2)
    // -----------------------------------------------------------------------

    [Fact]
    public void OrcishBowmasters_HasTwoTriggeredAbilities_V2()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        // The single printed line is wired as TWO sibling triggers — one
        // for ETB (CardMovedEvent) and one for the opponent-draw clause
        // (CardDrawnEvent) — to satisfy the TriggerManager's
        // subscribe-by-EventType contract. Both share the resolve body.
        ob.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB + opponent-draw triggers are the v2 wiring");
    }

    [Fact]
    public void OrcishBowmasters_HasNoActivatedAbilities()
    {
        var ob = OrcishBowmastersFactory.Create(_alice);

        ob.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Orcish Bowmasters has no activated abilities");
    }
}
