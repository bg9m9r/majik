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
/// Unit tests for <see cref="SnowCoveredPlainsFactory"/>.
///
/// Snow-Covered Plains: Basic Snow Land — Plains.
/// Oracle: ({T}: Add {W}.) — intrinsic to the Plains subtype (CR 305.6).
///
/// Snow-Covered Plains carries two supertypes — Basic AND Snow (CR 205.4d) —
/// plus the Plains land subtype. The Snow supertype matters for cards that
/// care about snow permanents or snow mana (e.g. Skred, Dead of Winter,
/// Rime Tender).
///
/// Covers:
/// - Identity: Land type, Basic AND Snow supertypes (CR 205.4d), Plains subtype.
/// - Owner / controller wiring.
/// - {T}: Add {W} mana ability present from the JSON-driven build route.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
[Trait("Color", "C")]
public class SnowCoveredPlainsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — JSON-driven, carries mana ability)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredPlains_IsALand()
    {
        var plains = (Land)NamedCardFactory.Create("Snow-Covered Plains", _alice);

        plains.HasType(CardType.Land).Should().BeTrue(
            "Snow-Covered Plains is a Land (CR 305.1)");
    }

    [Fact]
    public void SnowCoveredPlains_HasBasicSupertype()
    {
        var plains = (Land)NamedCardFactory.Create("Snow-Covered Plains", _alice);

        plains.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Snow-Covered Plains has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void SnowCoveredPlains_HasSnowSupertype()
    {
        var plains = (Land)NamedCardFactory.Create("Snow-Covered Plains", _alice);

        plains.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Snow-Covered Plains has the Snow supertype (CR 205.4d)");
    }

    [Fact]
    public void SnowCoveredPlains_HasPlainsSubtype()
    {
        var plains = (Land)NamedCardFactory.Create("Snow-Covered Plains", _alice);

        plains.HasSubtype(CardSubtype.Plains).Should().BeTrue(
            "Snow-Covered Plains is a Plains land (CR 205.3i)");
    }

    [Fact]
    public void SnowCoveredPlains_HasCorrectName()
    {
        var plains = (Land)NamedCardFactory.Create("Snow-Covered Plains", _alice);

        plains.Name.Should().Be("Snow-Covered Plains");
    }

    [Fact]
    public void SnowCoveredPlains_OwnerAndControllerAreSet()
    {
        var plains = (Land)NamedCardFactory.Create("Snow-Covered Plains", _alice);

        plains.Owner.Should().BeSameAs(_alice);
        plains.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — produced directly by the JSON-driven build route
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredPlains_HasWhiteManaAbility()
    {
        var plains = (Land)NamedCardFactory.Create("Snow-Covered Plains", _alice);

        var manaAbilities = plains.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {W} is the single mana ability for a Basic Plains (CR 305.6)");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{W} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredPlains_ThrowsOnNullOwner()
    {
        var act = () => (Land)NamedCardFactory.Create("Snow-Covered Plains", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
