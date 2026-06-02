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
/// Unit tests for <see cref="SnowCoveredForestFactory"/>.
///
/// Snow-Covered Forest: Basic Snow Land — Forest.
/// Oracle: {T}: Add {G} (intrinsic to the Forest subtype — CR 305.6).
///
/// Covers:
/// - Identity: Land type, Basic AND Snow supertypes (CR 205.4d), Forest subtype.
/// - Owner / controller wiring.
/// - {T}: Add {G} mana ability present when created via NamedCardFactory.
/// - NamedCardFactory dispatch resolves the printed name.
/// </summary>
[Trait("Color", "C")]
public class SnowCoveredForestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (via factory direct create — no mana ability yet)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredForest_IsALand()
    {
        var forest = SnowCoveredForestFactory.Create(_alice);

        forest.HasType(CardType.Land).Should().BeTrue(
            "Snow-Covered Forest is a Land (CR 305.1)");
    }

    [Fact]
    public void SnowCoveredForest_HasBasicSupertype()
    {
        var forest = SnowCoveredForestFactory.Create(_alice);

        forest.HasSupertype(CardSupertype.Basic).Should().BeTrue(
            "Snow-Covered Forest has the Basic supertype (CR 205.4)");
    }

    [Fact]
    public void SnowCoveredForest_HasSnowSupertype()
    {
        var forest = SnowCoveredForestFactory.Create(_alice);

        forest.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Snow-Covered Forest has the Snow supertype (CR 205.4d)");
    }

    [Fact]
    public void SnowCoveredForest_HasForestSubtype()
    {
        var forest = SnowCoveredForestFactory.Create(_alice);

        forest.HasSubtype(CardSubtype.Forest).Should().BeTrue(
            "Snow-Covered Forest is a Forest land (CR 205.3i)");
    }

    [Fact]
    public void SnowCoveredForest_HasCorrectName()
    {
        var forest = SnowCoveredForestFactory.Create(_alice);

        forest.Name.Should().Be("Snow-Covered Forest");
    }

    [Fact]
    public void SnowCoveredForest_OwnerAndControllerAreSet()
    {
        var forest = SnowCoveredForestFactory.Create(_alice);

        forest.Owner.Should().BeSameAs(_alice);
        forest.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — requires NamedCardFactory dispatch (AttachBasicLandMana)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredForest_HasGreenManaAbility_WhenCreatedViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Snow-Covered Forest", _alice);

        var manaAbilities = card.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "{T}: Add {G} is the single mana ability for a Basic Forest");

        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1,
            "{G} produces exactly one mana");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCoveredForest_ThrowsOnNullOwner()
    {
        var act = () => SnowCoveredForestFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
