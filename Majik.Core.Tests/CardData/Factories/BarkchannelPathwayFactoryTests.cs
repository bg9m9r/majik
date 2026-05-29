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
/// Tests for <see cref="BarkchannelPathwayFactory"/> and
/// <see cref="TidechannelPathwayFactory"/> — the front + back faces of the
/// Kaldheim modal double-faced "Pathway" dual land
/// Barkchannel Pathway // Tidechannel Pathway.
///
/// Front face (Barkchannel Pathway):
///   Land. "{T}: Add {G}."
///
/// Back face (Tidechannel Pathway):
///   Land. "{T}: Add {U}."
///
/// Both faces are plain "{T}: Add &lt;C&gt;" lands — no ETB-tapped clause and
/// no other text, so neither face carries a replacement effect.
///
/// Covers:
/// - Identity for both faces (name, type Land, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: single {T}: Add {G} mana ability, no triggers/replacements.
/// - Back: single {T}: Add {U} mana ability, no triggers.
/// </summary>
public class BarkchannelPathwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — Barkchannel Pathway
    // =========================================================================

    [Fact]
    public void BarkchannelPathway_Identity_Land_OnFrontFace()
    {
        var land = BarkchannelPathwayFactory.Create(_alice);

        land.Name.Should().Be("Barkchannel Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BarkchannelPathway_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Barkchannel Pathway", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Barkchannel Pathway");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void BarkchannelPathway_HasMdfcTracker_OnFrontFace()
    {
        var land = BarkchannelPathwayFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Barkchannel Pathway");
        land.MdfcState.BackFaceName.Should().Be("Tidechannel Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse();
        land.MdfcState.ActiveFaceName.Should().Be("Barkchannel Pathway");
    }

    [Fact]
    public void BarkchannelPathway_HasTapForGreenManaAbility()
    {
        var land = BarkchannelPathwayFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        // {T}: Add {G} — one green mana (CR 605.1).
        mana.ManaGenerated.Green.Should().Be(1);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.TotalValue.Should().Be(1, "{T}: Add {G} produces exactly one mana");

        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("a Pathway face has no triggered ability");
    }

    // =========================================================================
    // Back face — Tidechannel Pathway
    // =========================================================================

    [Fact]
    public void TidechannelPathway_Identity_Land_OnBackFace()
    {
        var land = TidechannelPathwayFactory.Create(_alice);

        land.Name.Should().Be("Tidechannel Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Barkchannel Pathway");
        land.MdfcState.BackFaceName.Should().Be("Tidechannel Pathway");
        land.MdfcState.IsBackFace.Should().BeTrue();
        land.MdfcState.ActiveFaceName.Should().Be("Tidechannel Pathway");
    }

    [Fact]
    public void TidechannelPathway_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Tidechannel Pathway", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Tidechannel Pathway");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void TidechannelPathway_HasTapForBlueManaAbility()
    {
        var land = TidechannelPathwayFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        // {T}: Add {U} — one blue mana (CR 605.1).
        mana.ManaGenerated.Blue.Should().Be(1);
        mana.ManaGenerated.Green.Should().Be(0);
        mana.ManaGenerated.TotalValue.Should().Be(1, "{T}: Add {U} produces exactly one mana");

        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("a Pathway face has no triggered ability");
    }
}
