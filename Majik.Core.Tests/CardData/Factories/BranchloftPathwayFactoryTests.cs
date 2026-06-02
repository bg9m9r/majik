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
/// Tests for <see cref="BranchloftPathwayFactory"/> and
/// <see cref="BoulderloftPathwayFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced "Pathway" dual land
/// Branchloft Pathway // Boulderloft Pathway.
///
/// Front face (Branchloft Pathway):
///   Land. "{T}: Add {G}."
///
/// Back face (Boulderloft Pathway):
///   Land. "{T}: Add {W}."
///
/// Both faces are plain "{T}: Add &lt;C&gt;" lands — no ETB-tapped clause and
/// no other text, so neither face carries a replacement effect.
///
/// Covers:
/// - Identity for both faces (name, type Land, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: single {T}: Add {G} mana ability, no triggers/replacements.
/// - Back: single {T}: Add {W} mana ability, no triggers.
/// </summary>
[Trait("Color", "C")]
public class BranchloftPathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Branchloft Pathway
    // =========================================================================

    [Fact]
    public void BranchloftPathway_Identity_Land_OnFrontFace()
    {
        var land = BranchloftPathwayFactory.Create(_alice);

        land.Name.Should().Be("Branchloft Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BranchloftPathway_HasMdfcTracker_OnFrontFace()
    {
        var land = BranchloftPathwayFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Branchloft Pathway");
        land.MdfcState.BackFaceName.Should().Be("Boulderloft Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse();
        land.MdfcState.ActiveFaceName.Should().Be("Branchloft Pathway");
    }

    [Fact]
    public void BranchloftPathway_HasTapForGreenManaAbility()
    {
        var land = BranchloftPathwayFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        // {T}: Add {G} — one green mana (CR 605.1).
        mana.ManaGenerated.Green.Should().Be(1);
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.TotalValue.Should().Be(1, "{T}: Add {G} produces exactly one mana");

        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("a Pathway face has no triggered ability");
    }

    // =========================================================================
    // Back face — Boulderloft Pathway
    // =========================================================================

    [Fact]
    public void BoulderloftPathway_Identity_Land_OnBackFace()
    {
        var land = BoulderloftPathwayFactory.Create(_alice);

        land.Name.Should().Be("Boulderloft Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Branchloft Pathway");
        land.MdfcState.BackFaceName.Should().Be("Boulderloft Pathway");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Boulderloft Pathway");
    }
    [Fact]
    public void BoulderloftPathway_HasTapForWhiteManaAbility()
    {
        var land = BoulderloftPathwayFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        // {T}: Add {W} — one white mana (CR 605.1).
        mana.ManaGenerated.White.Should().Be(1);
        mana.ManaGenerated.Green.Should().Be(0);
        mana.ManaGenerated.TotalValue.Should().Be(1, "{T}: Add {W} produces exactly one mana");

        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("a Pathway face has no triggered ability");
    }
}
