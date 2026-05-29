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
/// Unit tests for <see cref="SwampFactory"/>.
///
/// Swamp: Basic Land — Swamp.
/// Oracle: ({T}: Add {B}.) — intrinsic to the Swamp subtype (CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic supertype (CR 205.4), Swamp subtype (CR 205.3i).
/// - Owner / controller wiring.
/// - {T}: Add {B} mana ability present from the JSON-driven build route.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
public class SwampFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — JSON-driven, carries mana ability)
    // -----------------------------------------------------------------------

    [Fact]
    public void Swamp_IsALand()
    {
        var swamp = SwampFactory.Create(_alice);

        swamp.HasType(CardType.Land).Should().BeTrue(
            "Swamp is a Land (CR 305.1)");
    }

    [Fact]
    public void Swamp_HasBasicSupertype()
    {
        var swamp = SwampFactory.Create(_alice);

        swamp.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Swamp has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void Swamp_HasSwampSubtype()
    {
        var swamp = SwampFactory.Create(_alice);

        swamp.HasSubtype(CardSubtype.Swamp).Should().BeTrue(
            "Swamp is a Swamp land (CR 205.3i)");
    }

    [Fact]
    public void Swamp_HasCorrectName()
    {
        var swamp = SwampFactory.Create(_alice);

        swamp.Name.Should().Be("Swamp");
    }

    [Fact]
    public void Swamp_OwnerAndControllerAreSet()
    {
        var swamp = SwampFactory.Create(_alice);

        swamp.Owner.Should().BeSameAs(_alice);
        swamp.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — produced directly by the JSON-driven build route
    // -----------------------------------------------------------------------

    [Fact]
    public void Swamp_HasBlackManaAbility()
    {
        var swamp = SwampFactory.Create(_alice);

        var manaAbilities = swamp.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {B} is the single mana ability for a Basic Swamp (CR 305.6)");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{B} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Swamp_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Swamp", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Swamp");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.HasSubtype(CardSubtype.Swamp).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the dispatched Swamp carries its {T}: Add {B} mana ability");
    }

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void Swamp_ThrowsOnNullOwner()
    {
        var act = () => SwampFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
