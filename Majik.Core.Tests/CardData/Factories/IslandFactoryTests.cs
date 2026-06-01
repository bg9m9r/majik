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
/// Unit tests for <see cref="IslandFactory"/>.
///
/// Island: Basic Land — Island.
/// Oracle: ({T}: Add {U}.) — intrinsic to the Island subtype (CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic supertype (CR 205.4), Island subtype (CR 205.3i).
/// - Owner / controller wiring.
/// - {T}: Add {U} mana ability present from the JSON-driven build route.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
public class IslandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — JSON-driven, carries mana ability)
    // -----------------------------------------------------------------------

    [Fact]
    public void Island_IsALand()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);

        island.HasType(CardType.Land).Should().BeTrue(
            "Island is a Land (CR 305.1)");
    }

    [Fact]
    public void Island_HasBasicSupertype()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);

        island.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Island has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void Island_HasIslandSubtype()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);

        island.HasSubtype(CardSubtype.Island).Should().BeTrue(
            "Island is an Island land (CR 205.3i)");
    }

    [Fact]
    public void Island_HasCorrectName()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);

        island.Name.Should().Be("Island");
    }

    [Fact]
    public void Island_OwnerAndControllerAreSet()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);

        island.Owner.Should().BeSameAs(_alice);
        island.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — produced directly by the JSON-driven build route
    // -----------------------------------------------------------------------

    [Fact]
    public void Island_HasBlueManaAbility()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);

        var manaAbilities = island.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {U} is the single mana ability for a Basic Island (CR 305.6)");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{U} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Island_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Island", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Island");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.HasSubtype(CardSubtype.Island).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the dispatched Island carries its {T}: Add {U} mana ability");
    }

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void Island_ThrowsOnNullOwner()
    {
        var act = () => (Land)NamedCardFactory.Create("Island", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
