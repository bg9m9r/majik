using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BrightclimbPathwayFactory"/> and
/// <see cref="GrimclimbPathwayFactory"/> — the front + back faces of the
/// Kaldheim modal double-faced card
/// Brightclimb Pathway // Grimclimb Pathway.
///
/// Front face (Brightclimb Pathway):
///   Land. "{T}: Add {W}."
///
/// Back face (Grimclimb Pathway):
///   Land. "{T}: Add {B}."
///
/// Both faces are plain (non-basic, no subtype, no supertype) lands with a
/// single tap-for-one-mana ability (CR 605.1 — mana ability, no stack).
/// Neither face enters tapped and neither carries any other ability.
///
/// Covers:
/// - Identity for both faces (name, type, owner/controller, not legendary).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Each face has exactly one mana ability producing the right color.
/// - No triggered / non-mana activated abilities ship.
/// </summary>
[Trait("Color", "C")]
public class BrightclimbPathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Brightclimb Pathway
    // =========================================================================

    [Fact]
    public void BrightclimbPathway_Identity_Land_OnFrontFace()
    {
        var land = BrightclimbPathwayFactory.Create(_alice);

        land.Name.Should().Be("Brightclimb Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Brightclimb Pathway");
        land.MdfcState.BackFaceName.Should().Be("Grimclimb Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse();
        land.MdfcState.ActiveFaceName.Should().Be("Brightclimb Pathway");
    }
    [Fact]
    public void BrightclimbPathway_HasSingleTapForWhiteManaAbility()
    {
        var land = BrightclimbPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void BrightclimbPathway_HasNoOtherAbilities()
    {
        var land = BrightclimbPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // =========================================================================
    // Back face — Grimclimb Pathway
    // =========================================================================

    [Fact]
    public void GrimclimbPathway_Identity_Land_OnBackFace()
    {
        var land = GrimclimbPathwayFactory.Create(_alice);

        land.Name.Should().Be("Grimclimb Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Brightclimb Pathway");
        land.MdfcState.BackFaceName.Should().Be("Grimclimb Pathway");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Grimclimb Pathway");
    }
    [Fact]
    public void GrimclimbPathway_HasSingleTapForBlackManaAbility()
    {
        var land = GrimclimbPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void GrimclimbPathway_HasNoOtherAbilities()
    {
        var land = GrimclimbPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
