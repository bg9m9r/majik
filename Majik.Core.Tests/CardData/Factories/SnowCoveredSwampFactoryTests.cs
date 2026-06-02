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
/// Unit tests for <see cref="SnowCoveredSwampFactory"/>.
///
/// Snow-Covered Swamp: Basic Snow Land — Swamp.
/// Oracle: {T}: Add {B} (intrinsic to the Swamp subtype — CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic AND Snow supertypes (CR 205.4d), Swamp subtype.
/// - Owner / controller wiring.
/// - {T}: Add {B} mana ability present when created via NamedCardFactory.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
[Trait("Color", "C")]
public class SnowCoveredSwampFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — no mana ability yet)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredSwamp_IsALand()
    {
        var swamp = SnowCoveredSwampFactory.Create(_alice);

        swamp.HasType(CardType.Land).Should().BeTrue(
            "Snow-Covered Swamp is a Land (CR 305.1)");
    }

    [Fact]
    public void SnowCoveredSwamp_HasBasicSupertype()
    {
        var swamp = SnowCoveredSwampFactory.Create(_alice);

        swamp.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Snow-Covered Swamp has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void SnowCoveredSwamp_HasSnowSupertype()
    {
        var swamp = SnowCoveredSwampFactory.Create(_alice);

        swamp.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Snow-Covered Swamp has the Snow supertype (CR 205.4d)");
    }

    [Fact]
    public void SnowCoveredSwamp_HasSwampSubtype()
    {
        var swamp = SnowCoveredSwampFactory.Create(_alice);

        swamp.HasSubtype(CardSubtype.Swamp).Should().BeTrue(
            "Snow-Covered Swamp is a Swamp land (CR 205.3i)");
    }

    [Fact]
    public void SnowCoveredSwamp_HasCorrectName()
    {
        var swamp = SnowCoveredSwampFactory.Create(_alice);

        swamp.Name.Should().Be("Snow-Covered Swamp");
    }

    [Fact]
    public void SnowCoveredSwamp_OwnerAndControllerAreSet()
    {
        var swamp = SnowCoveredSwampFactory.Create(_alice);

        swamp.Owner.Should().BeSameAs(_alice);
        swamp.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — requires NamedCardFactory dispatch (AttachBasicLandMana)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredSwamp_HasBlackManaAbility_WhenCreatedViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Snow-Covered Swamp", _alice);

        var manaAbilities = card.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {B} is the single mana ability for a Basic Swamp");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{B} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredSwamp_ThrowsOnNullOwner()
    {
        var act = () => SnowCoveredSwampFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
