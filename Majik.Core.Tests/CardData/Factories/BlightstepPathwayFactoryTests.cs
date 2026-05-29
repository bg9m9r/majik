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
/// Tests for <see cref="BlightstepPathwayFactory"/> and
/// <see cref="SearstepPathwayFactory"/> — the front + back faces of the
/// Kaldheim land // land modal double-faced card
/// Blightstep Pathway // Searstep Pathway.
///
/// Front face (Blightstep Pathway):
///   Land. "{T}: Add {B}."
///
/// Back face (Searstep Pathway):
///   Land. "{T}: Add {R}."
///
/// Covers:
/// - Both-face identity (name, Land type, non-basic, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face tracker (front starts on front; back pre-flipped).
/// - Each face has exactly one {T}: Add {C} mana ability, correct colour.
/// - No non-mana activated / triggered abilities (pathways are vanilla).
/// </summary>
public class BlightstepPathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Blightstep Pathway
    // =========================================================================

    [Fact]
    public void BlightstepPathway_Identity_NonBasicLand()
    {
        var land = BlightstepPathwayFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Blightstep Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Blightstep Pathway is a non-basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BlightstepPathway()
    {
        var card = NamedCardFactory.Create("Blightstep Pathway", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Blightstep Pathway");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void BlightstepPathway_CarriesMdfcState_FrontFace()
    {
        var land = BlightstepPathwayFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Blightstep Pathway is the front face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Blightstep Pathway");
        land.MdfcState!.BackFaceName.Should().Be("Searstep Pathway");
        land.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        land.MdfcState!.ActiveFaceName.Should().Be("Blightstep Pathway");
    }

    [Fact]
    public void BlightstepPathway_HasSingleBlackManaAbility()
    {
        var land = BlightstepPathwayFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "Blightstep Pathway has {T}: Add {B}");

        var black = ManaCost.Parse("B");
        manaAbilities[0].ManaGenerated.Black.Should().Be(black.Black);
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0,
            "Blightstep Pathway produces only black mana");
    }

    [Fact]
    public void BlightstepPathway_HasNoNonManaAbilities()
    {
        var land = BlightstepPathwayFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("pathways have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("pathways enter untapped — no ETB trigger (CR 305.4)");
    }

    // =========================================================================
    // Back face — Searstep Pathway
    // =========================================================================

    [Fact]
    public void SearstepPathway_Identity_NonBasicLand()
    {
        var land = SearstepPathwayFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Searstep Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Searstep Pathway is a non-basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SearstepPathway()
    {
        var card = NamedCardFactory.Create("Searstep Pathway", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Searstep Pathway");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SearstepPathway_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = SearstepPathwayFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Searstep Pathway is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Blightstep Pathway");
        land.MdfcState!.BackFaceName.Should().Be("Searstep Pathway");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Searstep Pathway");
    }

    [Fact]
    public void SearstepPathway_HasSingleRedManaAbility()
    {
        var land = SearstepPathwayFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "Searstep Pathway has {T}: Add {R}");

        var red = ManaCost.Parse("R");
        manaAbilities[0].ManaGenerated.Red.Should().Be(red.Red);
        manaAbilities[0].ManaGenerated.Red.Should().BeGreaterThan(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0,
            "Searstep Pathway produces only red mana");
    }

    [Fact]
    public void SearstepPathway_HasNoNonManaAbilities()
    {
        var land = SearstepPathwayFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("pathways have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("pathways enter untapped — no ETB trigger (CR 305.4)");
    }
}
