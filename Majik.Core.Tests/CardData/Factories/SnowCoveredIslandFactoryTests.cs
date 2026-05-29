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
/// Unit tests for <see cref="SnowCoveredIslandFactory"/>.
///
/// Snow-Covered Island: Basic Snow Land — Island.
/// Oracle: {T}: Add {U} (intrinsic to the Island subtype — CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic AND Snow supertypes (CR 205.4d), Island subtype.
/// - Owner / controller wiring.
/// - {T}: Add {U} mana ability present when created via NamedCardFactory.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
public class SnowCoveredIslandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — no mana ability yet)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredIsland_IsALand()
    {
        var island = SnowCoveredIslandFactory.Create(_alice);

        island.HasType(CardType.Land).Should().BeTrue(
            "Snow-Covered Island is a Land (CR 305.1)");
    }

    [Fact]
    public void SnowCoveredIsland_HasBasicSupertype()
    {
        var island = SnowCoveredIslandFactory.Create(_alice);

        island.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Snow-Covered Island has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void SnowCoveredIsland_HasSnowSupertype()
    {
        var island = SnowCoveredIslandFactory.Create(_alice);

        island.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Snow-Covered Island has the Snow supertype (CR 205.4d)");
    }

    [Fact]
    public void SnowCoveredIsland_HasIslandSubtype()
    {
        var island = SnowCoveredIslandFactory.Create(_alice);

        island.HasSubtype(CardSubtype.Island).Should().BeTrue(
            "Snow-Covered Island is an Island land (CR 205.3i)");
    }

    [Fact]
    public void SnowCoveredIsland_HasCorrectName()
    {
        var island = SnowCoveredIslandFactory.Create(_alice);

        island.Name.Should().Be("Snow-Covered Island");
    }

    [Fact]
    public void SnowCoveredIsland_OwnerAndControllerAreSet()
    {
        var island = SnowCoveredIslandFactory.Create(_alice);

        island.Owner.Should().BeSameAs(_alice);
        island.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — requires NamedCardFactory dispatch (AttachBasicLandMana)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredIsland_HasBlueManaAbility_WhenCreatedViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Snow-Covered Island", _alice);

        var manaAbilities = card.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {U} is the single mana ability for a Basic Island");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{U} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredIsland_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Snow-Covered Island", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Snow-Covered Island");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.HasSupertype(CardSupertype.Snow).Should().BeTrue();
        card.HasSubtype(CardSubtype.Island).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredIsland_ThrowsOnNullOwner()
    {
        var act = () => SnowCoveredIslandFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
