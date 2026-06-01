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
/// Unit tests for <see cref="WastesFactory"/>.
///
/// Wastes: Basic Land (no land subtype).
/// Oracle: {T}: Add {C}.
///
/// Wastes is the only basic land without a land subtype (CR 205.3i does not
/// list it among the basic land types), so its "{T}: Add {C}" ability is
/// printed directly rather than granted intrinsically by CR 305.6.
///
/// Covers:
/// - Identity: Land type, Basic supertype (CR 205.4), no land subtype.
/// - Owner / controller wiring.
/// - {T}: Add {C} mana ability present from the JSON-driven build route.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
public class WastesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — JSON-driven, carries mana ability)
    // -----------------------------------------------------------------------

    [Fact]
    public void Wastes_IsALand()
    {
        var wastes = (Land)NamedCardFactory.Create("Wastes", _alice);

        wastes.HasType(CardType.Land).Should().BeTrue(
            "Wastes is a Land (CR 305.1)");
    }

    [Fact]
    public void Wastes_HasBasicSupertype()
    {
        var wastes = (Land)NamedCardFactory.Create("Wastes", _alice);

        wastes.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Wastes has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void Wastes_HasNoLandSubtype()
    {
        var wastes = (Land)NamedCardFactory.Create("Wastes", _alice);

        wastes.Subtypes.Should().BeEmpty(
            "Wastes is a basic land with no land subtype (CR 205.3i)");
    }

    [Fact]
    public void Wastes_HasCorrectName()
    {
        var wastes = (Land)NamedCardFactory.Create("Wastes", _alice);

        wastes.Name.Should().Be("Wastes");
    }

    [Fact]
    public void Wastes_OwnerAndControllerAreSet()
    {
        var wastes = (Land)NamedCardFactory.Create("Wastes", _alice);

        wastes.Owner.Should().BeSameAs(_alice);
        wastes.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — produced directly by the JSON-driven build route
    // -----------------------------------------------------------------------

    [Fact]
    public void Wastes_HasColorlessManaAbility()
    {
        var wastes = (Land)NamedCardFactory.Create("Wastes", _alice);

        var manaAbilities = wastes.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {C} is the single mana ability printed on Wastes");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{C} produces exactly one (colorless) mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Wastes_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Wastes", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Wastes");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.Subtypes.Should().BeEmpty();
        card.Owner.Should().BeSameAs(_alice);

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the dispatched Wastes carries its {T}: Add {C} mana ability");
    }

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void Wastes_ThrowsOnNullOwner()
    {
        var act = () => (Land)NamedCardFactory.Create("Wastes", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
