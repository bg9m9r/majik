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
/// Unit tests for <see cref="PlainsFactory"/>.
///
/// Plains: Basic Land — Plains.
/// Oracle: ({T}: Add {W}.) — intrinsic to the Plains subtype (CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic supertype (CR 205.4), Plains subtype (CR 205.3i).
/// - Owner / controller wiring.
/// - {T}: Add {W} mana ability present from the JSON-driven build route.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
public class PlainsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — JSON-driven, carries mana ability)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plains_IsALand()
    {
        var plains = PlainsFactory.Create(_alice);

        plains.HasType(CardType.Land).Should().BeTrue(
            "Plains is a Land (CR 305.1)");
    }

    [Fact]
    public void Plains_HasBasicSupertype()
    {
        var plains = PlainsFactory.Create(_alice);

        plains.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Plains has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void Plains_HasPlainsSubtype()
    {
        var plains = PlainsFactory.Create(_alice);

        plains.HasSubtype(CardSubtype.Plains).Should().BeTrue(
            "Plains is a Plains land (CR 205.3i)");
    }

    [Fact]
    public void Plains_HasCorrectName()
    {
        var plains = PlainsFactory.Create(_alice);

        plains.Name.Should().Be("Plains");
    }

    [Fact]
    public void Plains_OwnerAndControllerAreSet()
    {
        var plains = PlainsFactory.Create(_alice);

        plains.Owner.Should().BeSameAs(_alice);
        plains.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — produced directly by the JSON-driven build route
    // -----------------------------------------------------------------------

    [Fact]
    public void Plains_HasWhiteManaAbility()
    {
        var plains = PlainsFactory.Create(_alice);

        var manaAbilities = plains.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {W} is the single mana ability for a Basic Plains (CR 305.6)");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{W} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Plains_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Plains", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Plains");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.HasSubtype(CardSubtype.Plains).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the dispatched Plains carries its {T}: Add {W} mana ability");
    }

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void Plains_ThrowsOnNullOwner()
    {
        var act = () => PlainsFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
