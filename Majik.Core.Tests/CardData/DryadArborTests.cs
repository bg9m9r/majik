using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="DryadArborFactory"/>.</summary>
public class DryadArborTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card types
    // -----------------------------------------------------------------------

    [Fact]
    public void DryadArbor_IsBothLandAndCreature()
    {
        var arbor = DryadArborFactory.Create(_alice);

        arbor.HasType(CardType.Land).Should().BeTrue("Dryad Arbor is a Land");
        arbor.HasType(CardType.Creature).Should().BeTrue("Dryad Arbor is a Creature");
    }

    // -----------------------------------------------------------------------
    // Power / toughness
    // -----------------------------------------------------------------------

    [Fact]
    public void DryadArbor_IsOneOne()
    {
        var arbor = DryadArborFactory.Create(_alice);

        arbor.Power.Should().Be(1);
        arbor.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Subtypes
    // -----------------------------------------------------------------------

    [Fact]
    public void DryadArbor_HasForestAndDryadSubtypes()
    {
        var arbor = DryadArborFactory.Create(_alice);

        arbor.HasSubtype(CardSubtype.Forest).Should().BeTrue("Dryad Arbor has the Forest land subtype");
        arbor.HasSubtype(CardSubtype.Dryad).Should().BeTrue("Dryad Arbor has the Dryad creature subtype");
    }

    // -----------------------------------------------------------------------
    // Mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void DryadArbor_HasGreenManaAbility()
    {
        var arbor = DryadArborFactory.Create(_alice);

        arbor.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "Dryad Arbor has exactly one {T}: Add {G} mana ability from its Forest subtype");
    }

    // -----------------------------------------------------------------------
    // Mana cost
    // -----------------------------------------------------------------------

    [Fact]
    public void DryadArbor_NoManaCost()
    {
        var arbor = DryadArborFactory.Create(_alice);

        arbor.ManaCost.Should().BeEmpty("Dryad Arbor has no mana cost — CR 305.8");
    }

    // -----------------------------------------------------------------------
    // Owner / controller
    // -----------------------------------------------------------------------

    [Fact]
    public void DryadArbor_OwnerAndControllerAreSet()
    {
        var arbor = DryadArborFactory.Create(_alice);

        arbor.Owner.Should().BeSameAs(_alice);
        arbor.Controller.Should().BeSameAs(_alice);
    }
}
