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
/// Tests for <see cref="ClearwaterPathwayFactory"/> and
/// <see cref="MurkwaterPathwayFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card
/// Clearwater Pathway // Murkwater Pathway.
///
/// Front face (Clearwater Pathway):
///   Land. "{T}: Add {U}."
///
/// Back face (Murkwater Pathway):
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
public class ClearwaterPathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Clearwater Pathway
    // =========================================================================

    [Fact]
    public void ClearwaterPathway_Identity_Land_OnFrontFace()
    {
        var land = ClearwaterPathwayFactory.Create(_alice);

        land.Name.Should().Be("Clearwater Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Clearwater Pathway");
        land.MdfcState.BackFaceName.Should().Be("Murkwater Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse();
        land.MdfcState.ActiveFaceName.Should().Be("Clearwater Pathway");
    }
    [Fact]
    public void ClearwaterPathway_HasSingleTapForBlueManaAbility()
    {
        var land = ClearwaterPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void ClearwaterPathway_HasNoOtherAbilities()
    {
        var land = ClearwaterPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // =========================================================================
    // Back face — Murkwater Pathway
    // =========================================================================

    [Fact]
    public void MurkwaterPathway_Identity_Land_OnBackFace()
    {
        var land = MurkwaterPathwayFactory.Create(_alice);

        land.Name.Should().Be("Murkwater Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Clearwater Pathway");
        land.MdfcState.BackFaceName.Should().Be("Murkwater Pathway");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Murkwater Pathway");
    }
    [Fact]
    public void MurkwaterPathway_HasSingleTapForBlackManaAbility()
    {
        var land = MurkwaterPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void MurkwaterPathway_HasNoOtherAbilities()
    {
        var land = MurkwaterPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
