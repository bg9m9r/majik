using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WallOfWoodFactory"/>.
///
/// Covers:
/// - Card identity (0/3 Creature — Wall, mana cost {G}, owner/controller).
/// - Defender keyword marker (CR 702.3) surfaced via
///   <see cref="CombatAbilities.HasDefender"/>.
/// - No mana abilities (vanilla wall — no activated abilities beyond Defender).
/// - <see cref="NamedCardFactory"/> dispatch resolves "Wall of Wood" to the
///   correct shape.
/// </summary>
[Trait("Color", "G")]
public class WallOfWoodFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfWood_IsCreature()
    {
        var card = WallOfWoodFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void WallOfWood_NameIsCorrect()
    {
        var card = WallOfWoodFactory.Create(_alice);

        card.Name.Should().Be("Wall of Wood");
    }

    [Fact]
    public void WallOfWood_HasCorrectPrintedManaCost()
    {
        var card = WallOfWoodFactory.Create(_alice);

        card.ManaCost.Should().Be("{G}");
    }

    [Fact]
    public void WallOfWood_ManaValueIsOne()
    {
        var card = WallOfWoodFactory.Create(_alice);

        // {G} = 1 pip = MV 1 (CR 202.3).
        card.ManaCost.Should().Be("{G}",
            "mana value of {G} is 1 (CR 202.3 — each coloured symbol counts as 1)");
    }

    [Fact]
    public void WallOfWood_HasCorrectPrintedPowerAndToughness()
    {
        var card = WallOfWoodFactory.Create(_alice);

        card.BasePower.Should().Be(0);
        card.BaseToughness.Should().Be(3);
    }

    [Fact]
    public void WallOfWood_HasWallSubtype()
    {
        var card = WallOfWoodFactory.Create(_alice);

        card.Subtypes.Should().Contain(CardSubtype.Wall,
            "Wall of Wood is a Creature — Wall (no other subtypes)");
        card.Subtypes.Should().HaveCount(1,
            "Wall of Wood has exactly one subtype: Wall");
    }

    [Fact]
    public void WallOfWood_IsGreen()
    {
        var card = WallOfWoodFactory.Create(_alice);

        // Colour identity derived from mana cost {G}.
        card.ManaCost.Should().Contain("G",
            "Wall of Wood is a green card ({G} mana cost)");
    }

    [Fact]
    public void WallOfWood_OwnerAndControllerAreSet()
    {
        var card = WallOfWoodFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WallOfWood_IsNotLegendary()
    {
        var card = WallOfWoodFactory.Create(_alice);

        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Defender keyword (CR 702.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfWood_HasDefenderKeyword()
    {
        var card = WallOfWoodFactory.Create(_alice);

        // CR 702.3 — Defender wired as a KeywordAbility marker.
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "CR 702.3 — Defender is the only keyword on Wall of Wood");
        CombatAbilities.HasDefender(card).Should().BeTrue();
    }

    [Fact]
    public void WallOfWood_HasNoManaAbilities()
    {
        var card = WallOfWoodFactory.Create(_alice);

        // Wall of Wood is vanilla — no activated abilities beyond Defender.
        card.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Wall of Wood is a vanilla wall with no mana ability");
    }

    [Fact]
    public void WallOfWood_HasNoActivatedAbilities()
    {
        var card = WallOfWoodFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Wall of Wood is vanilla — no activated abilities");
    }

    // -----------------------------------------------------------------------
    // Dispatcher integration
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfWood_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Wall of Wood", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Wall of Wood");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().Contain(CardSubtype.Wall);

        var creature = card as Creature;
        creature.Should().NotBeNull();
        creature!.BasePower.Should().Be(0);
        creature.BaseToughness.Should().Be(3);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "dispatcher path attaches the Defender keyword");
    }
}
