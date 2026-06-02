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
/// Unit tests for <see cref="MountainFactory"/>.
///
/// Mountain: Basic Land — Mountain.
/// Oracle: ({T}: Add {R}.) — intrinsic to the Mountain subtype (CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic supertype (CR 205.4), Mountain subtype (CR 205.3i).
/// - Owner / controller wiring.
/// - {T}: Add {R} mana ability present from the JSON-driven build route.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
[Trait("Color", "C")]
public class MountainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — JSON-driven, carries mana ability)
    // -----------------------------------------------------------------------

    [Fact]
    public void Mountain_IsALand()
    {
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);

        mountain.HasType(CardType.Land).Should().BeTrue(
            "Mountain is a Land (CR 305.1)");
    }

    [Fact]
    public void Mountain_HasBasicSupertype()
    {
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);

        mountain.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Mountain has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void Mountain_HasMountainSubtype()
    {
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);

        mountain.HasSubtype(CardSubtype.Mountain).Should().BeTrue(
            "Mountain is a Mountain land (CR 205.3i)");
    }

    [Fact]
    public void Mountain_HasCorrectName()
    {
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);

        mountain.Name.Should().Be("Mountain");
    }

    [Fact]
    public void Mountain_OwnerAndControllerAreSet()
    {
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);

        mountain.Owner.Should().BeSameAs(_alice);
        mountain.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — produced directly by the JSON-driven build route
    // -----------------------------------------------------------------------

    [Fact]
    public void Mountain_HasRedManaAbility()
    {
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);

        var manaAbilities = mountain.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {R} is the single mana ability for a Basic Mountain (CR 305.6)");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{R} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void Mountain_ThrowsOnNullOwner()
    {
        var act = () => (Land)NamedCardFactory.Create("Mountain", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
