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
/// Tests for <see cref="HengegatePathwayFactory"/> and
/// <see cref="MistgatePathwayFactory"/> — the front + back faces of the
/// Kaldheim modal double-faced card
/// Hengegate Pathway // Mistgate Pathway.
///
/// Front face (Hengegate Pathway):
///   Land. "{T}: Add {W}."
///
/// Back face (Mistgate Pathway):
///   Land. "{T}: Add {U}."
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
public class HengegatePathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Hengegate Pathway
    // =========================================================================

    [Fact]
    public void HengegatePathway_Identity_Land_OnFrontFace()
    {
        var land = HengegatePathwayFactory.Create(_alice);

        land.Name.Should().Be("Hengegate Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Hengegate Pathway");
        land.MdfcState.BackFaceName.Should().Be("Mistgate Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse();
        land.MdfcState.ActiveFaceName.Should().Be("Hengegate Pathway");
    }

    [Fact]
    public void HengegatePathway_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Hengegate Pathway", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hengegate Pathway");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void HengegatePathway_HasSingleTapForWhiteManaAbility()
    {
        var land = HengegatePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void HengegatePathway_HasNoOtherAbilities()
    {
        var land = HengegatePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // =========================================================================
    // Back face — Mistgate Pathway
    // =========================================================================

    [Fact]
    public void MistgatePathway_Identity_Land_OnBackFace()
    {
        var land = MistgatePathwayFactory.Create(_alice);

        land.Name.Should().Be("Mistgate Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Hengegate Pathway");
        land.MdfcState.BackFaceName.Should().Be("Mistgate Pathway");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Mistgate Pathway");
    }

    [Fact]
    public void MistgatePathway_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mistgate Pathway", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Mistgate Pathway");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void MistgatePathway_HasSingleTapForBlueManaAbility()
    {
        var land = MistgatePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void MistgatePathway_HasNoOtherAbilities()
    {
        var land = MistgatePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
