using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DarkborePathwayFactory"/> and
/// <see cref="SlitherborePathwayFactory"/> — the front + back faces of the
/// Kaldheim land // land modal double-faced card
/// Darkbore Pathway // Slitherbore Pathway.
///
/// Front face (Darkbore Pathway):
///   Land. "{T}: Add {B}."
///
/// Back face (Slitherbore Pathway):
///   Land. "{T}: Add {G}."
///
/// Covers:
/// - Both-face identity (name, Land type, non-basic, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face tracker (front starts on front; back pre-flipped).
/// - Each face has exactly one {T}: Add {C} mana ability, correct colour.
/// - No non-mana activated / triggered abilities (pathways are vanilla).
/// </summary>
[Trait("Color", "C")]
public class DarkborePathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Darkbore Pathway
    // =========================================================================

    [Fact]
    public void DarkborePathway_Identity_NonBasicLand()
    {
        var land = DarkborePathwayFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Darkbore Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Darkbore Pathway is a non-basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void DarkborePathway_CarriesMdfcState_FrontFace()
    {
        var land = DarkborePathwayFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Darkbore Pathway is the front face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Darkbore Pathway");
        land.MdfcState!.BackFaceName.Should().Be("Slitherbore Pathway");
        land.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        land.MdfcState!.ActiveFaceName.Should().Be("Darkbore Pathway");
    }

    [Fact]
    public void DarkborePathway_HasSingleBlackManaAbility()
    {
        var land = DarkborePathwayFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "Darkbore Pathway has {T}: Add {B}");

        var black = ManaCost.Parse("B");
        manaAbilities[0].ManaGenerated.Black.Should().Be(black.Black);
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0,
            "Darkbore Pathway produces only black mana");
    }

    [Fact]
    public void DarkborePathway_HasNoNonManaAbilities()
    {
        var land = DarkborePathwayFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("pathways have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("pathways enter untapped — no ETB trigger (CR 305.4)");
    }

    // =========================================================================
    // Back face — Slitherbore Pathway
    // =========================================================================

    [Fact]
    public void SlitherborePathway_Identity_NonBasicLand()
    {
        var land = SlitherborePathwayFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Slitherbore Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Slitherbore Pathway is a non-basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SlitherborePathway_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = SlitherborePathwayFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Slitherbore Pathway is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Darkbore Pathway");
        land.MdfcState!.BackFaceName.Should().Be("Slitherbore Pathway");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Slitherbore Pathway");
    }

    [Fact]
    public void SlitherborePathway_HasSingleGreenManaAbility()
    {
        var land = SlitherborePathwayFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "Slitherbore Pathway has {T}: Add {G}");

        var green = ManaCost.Parse("G");
        manaAbilities[0].ManaGenerated.Green.Should().Be(green.Green);
        manaAbilities[0].ManaGenerated.Green.Should().BeGreaterThan(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0,
            "Slitherbore Pathway produces only green mana");
    }

    [Fact]
    public void SlitherborePathway_HasNoNonManaAbilities()
    {
        var land = SlitherborePathwayFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("pathways have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("pathways enter untapped — no ETB trigger (CR 305.4)");
    }
}
