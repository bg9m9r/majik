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
/// Tests for <see cref="RiverglidePathwayFactory"/> and
/// <see cref="LavaglidePathwayFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card
/// Riverglide Pathway // Lavaglide Pathway.
///
/// Front face (Riverglide Pathway):
///   Land. "{T}: Add {U}."
///
/// Back face (Lavaglide Pathway):
///   Land. "{T}: Add {R}."
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
public class RiverglidePathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Riverglide Pathway
    // =========================================================================

    [Fact]
    public void RiverglidePathway_Identity_Land_OnFrontFace()
    {
        var land = RiverglidePathwayFactory.Create(_alice);

        land.Name.Should().Be("Riverglide Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Riverglide Pathway");
        land.MdfcState.BackFaceName.Should().Be("Lavaglide Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse();
        land.MdfcState.ActiveFaceName.Should().Be("Riverglide Pathway");
    }
    [Fact]
    public void RiverglidePathway_HasSingleTapForBlueManaAbility()
    {
        var land = RiverglidePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void RiverglidePathway_HasNoOtherAbilities()
    {
        var land = RiverglidePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // =========================================================================
    // Back face — Lavaglide Pathway
    // =========================================================================

    [Fact]
    public void LavaglidePathway_Identity_Land_OnBackFace()
    {
        var land = LavaglidePathwayFactory.Create(_alice);

        land.Name.Should().Be("Lavaglide Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Riverglide Pathway");
        land.MdfcState.BackFaceName.Should().Be("Lavaglide Pathway");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Lavaglide Pathway");
    }
    [Fact]
    public void LavaglidePathway_HasSingleTapForRedManaAbility()
    {
        var land = LavaglidePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void LavaglidePathway_HasNoOtherAbilities()
    {
        var land = LavaglidePathwayFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
