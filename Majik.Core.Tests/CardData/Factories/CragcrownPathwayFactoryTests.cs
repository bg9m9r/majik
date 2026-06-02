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
/// Tests for <see cref="CragcrownPathwayFactory"/> and
/// <see cref="TimbercrownPathwayFactory"/> — the front + back faces of the
/// Kaldheim modal double-faced "Pathway" land cycle card
/// Cragcrown Pathway // Timbercrown Pathway.
///
/// Both faces are plain untapped lands (oracle text verified against
/// Scryfall, layout <c>modal_dfc</c>):
///   Front (Cragcrown Pathway): Land. "{T}: Add {R}."
///   Back  (Timbercrown Pathway): Land. "{T}: Add {G}."
///
/// Neither face enters tapped and neither carries any other ability — this
/// is the simplest MDFC shape (land // land, one mana ability per face),
/// strictly simpler than the Witch Enchanter // Witch-Blessed Meadow
/// analogue (which adds an ETB trigger + a painland life payment).
///
/// Covers:
/// - Identity for both faces (name, type = Land, no mana cost, owner).
/// - Each face has exactly one <see cref="ManaAbility"/> (CR 605.1) and no
///   triggered abilities (no ETB-tapped — Pathways enter untapped).
/// - <see cref="NamedCardFactory"/> dispatches both printed face names.
/// - MDFC face-tracker (front starts on the front face; back pre-flipped).
/// </summary>
[Trait("Color", "C")]
public class CragcrownPathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Cragcrown Pathway ({T}: Add {R})
    // =========================================================================

    [Fact]
    public void CragcrownPathway_Identity_Land_NoCost_FrontFace()
    {
        var land = CragcrownPathwayFactory.Create(_alice);

        land.Name.Should().Be("Cragcrown Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Cragcrown Pathway");
        land.MdfcState.BackFaceName.Should().Be("Timbercrown Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse();
        land.MdfcState.ActiveFaceName.Should().Be("Cragcrown Pathway");
    }

    [Fact]
    public void CragcrownPathway_HasSingleTapForRedManaAbility_NoTriggers()
    {
        var land = CragcrownPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("a Pathway land enters untapped — no ETB replacement/trigger");
    }
    // =========================================================================
    // Back face — Timbercrown Pathway ({T}: Add {G})
    // =========================================================================

    [Fact]
    public void TimbercrownPathway_Identity_Land_OnBackFace()
    {
        var land = TimbercrownPathwayFactory.Create(_alice);

        land.Name.Should().Be("Timbercrown Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Cragcrown Pathway");
        land.MdfcState.BackFaceName.Should().Be("Timbercrown Pathway");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Timbercrown Pathway");
    }

    [Fact]
    public void TimbercrownPathway_HasSingleTapForGreenManaAbility_NoTriggers()
    {
        var land = TimbercrownPathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("a Pathway land enters untapped — no ETB replacement/trigger");
    }
}
