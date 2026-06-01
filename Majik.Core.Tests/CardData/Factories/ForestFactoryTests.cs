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
/// Unit tests for <see cref="ForestFactory"/>.
///
/// Forest: Basic Land — Forest.
/// Oracle: ({T}: Add {G}.) — intrinsic to the Forest subtype (CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic supertype (CR 205.4), Forest subtype (CR 205.3i).
/// - Owner / controller wiring.
/// - {T}: Add {G} mana ability present from the JSON-driven build route.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
public class ForestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — JSON-driven, carries mana ability)
    // -----------------------------------------------------------------------

    [Fact]
    public void Forest_IsALand()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);

        forest.HasType(CardType.Land).Should().BeTrue(
            "Forest is a Land (CR 305.1)");
    }

    [Fact]
    public void Forest_HasBasicSupertype()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);

        forest.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Forest has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void Forest_HasForestSubtype()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);

        forest.HasSubtype(CardSubtype.Forest).Should().BeTrue(
            "Forest is a Forest land (CR 205.3i)");
    }

    [Fact]
    public void Forest_HasCorrectName()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);

        forest.Name.Should().Be("Forest");
    }

    [Fact]
    public void Forest_OwnerAndControllerAreSet()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);

        forest.Owner.Should().BeSameAs(_alice);
        forest.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — produced directly by the JSON-driven build route
    // -----------------------------------------------------------------------

    [Fact]
    public void Forest_HasGreenManaAbility()
    {
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);

        var manaAbilities = forest.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {G} is the single mana ability for a Basic Forest (CR 305.6)");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{G} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Forest_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Forest", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Forest");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.HasSubtype(CardSubtype.Forest).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the dispatched Forest carries its {T}: Add {G} mana ability");
    }

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void Forest_ThrowsOnNullOwner()
    {
        var act = () => (Land)NamedCardFactory.Create("Forest", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
