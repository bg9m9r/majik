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
/// Unit tests for <see cref="SnowCoveredWastesFactory"/>.
///
/// Snow-Covered Wastes: Basic Snow Land (no land subtype).
/// Oracle: {T}: Add {C}.
///
/// Like Wastes, Snow-Covered Wastes is a basic land with no land subtype (CR
/// 205.3i does not list it among the basic land types), so its "{T}: Add {C}"
/// ability is printed directly rather than granted intrinsically by CR 305.6.
/// It additionally carries the Snow supertype (CR 205.4d).
///
/// Covers:
/// - Identity: Land type, Basic AND Snow supertypes (CR 205.4 / 205.4d), no land subtype.
/// - Owner / controller wiring.
/// - {T}: Add {C} mana ability present from the JSON-driven build route.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
[Trait("Color", "C")]
public class SnowCoveredWastesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — JSON-driven, carries mana ability)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredWastes_IsALand()
    {
        var wastes = (Land)NamedCardFactory.Create("Snow-Covered Wastes", _alice);

        wastes.HasType(CardType.Land).Should().BeTrue(
            "Snow-Covered Wastes is a Land (CR 305.1)");
    }

    [Fact]
    public void SnowCoveredWastes_HasBasicSupertype()
    {
        var wastes = (Land)NamedCardFactory.Create("Snow-Covered Wastes", _alice);

        wastes.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Snow-Covered Wastes has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void SnowCoveredWastes_HasSnowSupertype()
    {
        var wastes = (Land)NamedCardFactory.Create("Snow-Covered Wastes", _alice);

        wastes.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Snow-Covered Wastes has the Snow supertype (CR 205.4d)");
    }

    [Fact]
    public void SnowCoveredWastes_HasNoLandSubtype()
    {
        var wastes = (Land)NamedCardFactory.Create("Snow-Covered Wastes", _alice);

        wastes.Subtypes.Should().BeEmpty(
            "Snow-Covered Wastes is a basic land with no land subtype (CR 205.3i)");
    }

    [Fact]
    public void SnowCoveredWastes_HasCorrectName()
    {
        var wastes = (Land)NamedCardFactory.Create("Snow-Covered Wastes", _alice);

        wastes.Name.Should().Be("Snow-Covered Wastes");
    }

    [Fact]
    public void SnowCoveredWastes_OwnerAndControllerAreSet()
    {
        var wastes = (Land)NamedCardFactory.Create("Snow-Covered Wastes", _alice);

        wastes.Owner.Should().BeSameAs(_alice);
        wastes.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — produced directly by the JSON-driven build route
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredWastes_HasColorlessManaAbility()
    {
        var wastes = (Land)NamedCardFactory.Create("Snow-Covered Wastes", _alice);

        var manaAbilities = wastes.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {C} is the single mana ability printed on Snow-Covered Wastes");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{C} produces exactly one (colorless) mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredWastes_ThrowsOnNullOwner()
    {
        var act = () => (Land)NamedCardFactory.Create("Snow-Covered Wastes", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
