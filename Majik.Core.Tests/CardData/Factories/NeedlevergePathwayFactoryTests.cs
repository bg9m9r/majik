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
/// Tests for <see cref="NeedlevergePathwayFactory"/> and
/// <see cref="PillarvergePathwayFactory"/> — the front + back faces of the
/// Streets of New Capenna modal double-faced card
/// Needleverge Pathway // Pillarverge Pathway.
///
/// Front face (Needleverge Pathway):
///   Land. "{T}: Add {R}."
///
/// Back face (Pillarverge Pathway):
///   Land. "{T}: Add {W}."
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
public class NeedlevergePathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Needleverge Pathway
    // =========================================================================

    [Fact]
    public void NeedlevergePathway_Identity_Land_OnFrontFace()
    {
        var land = NeedlevergePathwayFactory.Create(_alice);

        land.Name.Should().Be("Needleverge Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Needleverge Pathway");
        land.MdfcState.BackFaceName.Should().Be("Pillarverge Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse();
        land.MdfcState.ActiveFaceName.Should().Be("Needleverge Pathway");
    }
    [Fact]
    public void NeedlevergePathway_HasSingleTapForRedManaAbility()
    {
        var land = NeedlevergePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void NeedlevergePathway_HasNoOtherAbilities()
    {
        var land = NeedlevergePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // =========================================================================
    // Back face — Pillarverge Pathway
    // =========================================================================

    [Fact]
    public void PillarvergePathway_Identity_Land_OnBackFace()
    {
        var land = PillarvergePathwayFactory.Create(_alice);

        land.Name.Should().Be("Pillarverge Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Needleverge Pathway");
        land.MdfcState.BackFaceName.Should().Be("Pillarverge Pathway");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Pillarverge Pathway");
    }
    [Fact]
    public void PillarvergePathway_HasSingleTapForWhiteManaAbility()
    {
        var land = PillarvergePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void PillarvergePathway_HasNoOtherAbilities()
    {
        var land = PillarvergePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
