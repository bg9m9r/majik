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
/// Unit tests for <see cref="SnowCoveredMountainFactory"/>.
///
/// Snow-Covered Mountain: Basic Snow Land — Mountain.
/// Oracle: {T}: Add {R} (intrinsic to the Mountain subtype — CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic AND Snow supertypes (CR 205.4d), Mountain subtype.
/// - Owner / controller wiring.
/// - {T}: Add {R} mana ability present when created via NamedCardFactory.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
public class SnowCoveredMountainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — no mana ability yet)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredMountain_IsALand()
    {
        var mountain = SnowCoveredMountainFactory.Create(_alice);

        mountain.HasType(CardType.Land).Should().BeTrue(
            "Snow-Covered Mountain is a Land (CR 305.1)");
    }

    [Fact]
    public void SnowCoveredMountain_HasBasicSupertype()
    {
        var mountain = SnowCoveredMountainFactory.Create(_alice);

        mountain.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Snow-Covered Mountain has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void SnowCoveredMountain_HasSnowSupertype()
    {
        var mountain = SnowCoveredMountainFactory.Create(_alice);

        mountain.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Snow-Covered Mountain has the Snow supertype (CR 205.4d)");
    }

    [Fact]
    public void SnowCoveredMountain_HasMountainSubtype()
    {
        var mountain = SnowCoveredMountainFactory.Create(_alice);

        mountain.HasSubtype(CardSubtype.Mountain).Should().BeTrue(
            "Snow-Covered Mountain is a Mountain land (CR 205.3i)");
    }

    [Fact]
    public void SnowCoveredMountain_HasCorrectName()
    {
        var mountain = SnowCoveredMountainFactory.Create(_alice);

        mountain.Name.Should().Be("Snow-Covered Mountain");
    }

    [Fact]
    public void SnowCoveredMountain_OwnerAndControllerAreSet()
    {
        var mountain = SnowCoveredMountainFactory.Create(_alice);

        mountain.Owner.Should().BeSameAs(_alice);
        mountain.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — requires NamedCardFactory dispatch (AttachBasicLandMana)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredMountain_HasRedManaAbility_WhenCreatedViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Snow-Covered Mountain", _alice);

        var manaAbilities = card.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {R} is the single mana ability for a Basic Mountain");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{R} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredMountain_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Snow-Covered Mountain", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Snow-Covered Mountain");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.HasSupertype(CardSupertype.Snow).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mountain).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredMountain_ThrowsOnNullOwner()
    {
        var act = () => SnowCoveredMountainFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
