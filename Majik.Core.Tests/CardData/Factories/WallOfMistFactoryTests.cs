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
/// Unit tests for <see cref="WallOfMistFactory"/>.
///
/// Card: Wall of Mist — {1}{U} Creature — Wall 0/5.
/// Oracle text: "Defender."
///
/// Covers:
/// - Card identity ({1}{U}, blue, 0/5, Creature — Wall, mana value 2,
///   owner/controller wired).
/// - Defender keyword marker (CR 702.3) surfaced via
///   <see cref="CombatAbilities.HasDefender"/>.
/// - No activated abilities (vanilla wall).
/// - No triggered abilities (vanilla wall — no extra abilities beyond Defender).
/// - <see cref="NamedCardFactory"/> dispatch resolves "Wall of Mist" to the
///   correct shape.
/// </summary>
public class WallOfMistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfMist_IsCreature()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void WallOfMist_NameIsCorrect()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.Name.Should().Be("Wall of Mist");
    }

    [Fact]
    public void WallOfMist_HasCorrectPrintedManaCost()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.ManaCost.Should().Be("{1}{U}");
    }

    [Fact]
    public void WallOfMist_ManaValueIsTwo()
    {
        var card = WallOfMistFactory.Create(_alice);

        // {1}{U} = 2 pips = MV 2 (CR 202.3).
        card.ManaCost.Should().Be("{1}{U}",
            "mana value of {1}{U} is 2 (CR 202.3 — {1} generic + {U} coloured)");
    }

    [Fact]
    public void WallOfMist_HasCorrectPrintedPowerAndToughness()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.BasePower.Should().Be(0);
        card.BaseToughness.Should().Be(5);
    }

    [Fact]
    public void WallOfMist_HasWallSubtype()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.Subtypes.Should().Contain(CardSubtype.Wall,
            "Wall of Mist is a Creature — Wall (no other subtypes)");
        card.Subtypes.Should().HaveCount(1,
            "Wall of Mist has exactly one subtype: Wall");
    }

    [Fact]
    public void WallOfMist_IsBlue()
    {
        var card = WallOfMistFactory.Create(_alice);

        // Colour identity derived from mana cost {1}{U}.
        card.ManaCost.Should().Contain("U",
            "Wall of Mist is a blue card ({1}{U} mana cost)");
    }

    [Fact]
    public void WallOfMist_OwnerAndControllerAreSet()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WallOfMist_IsNotLegendary()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Defender keyword (CR 702.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfMist_HasDefenderKeyword()
    {
        var card = WallOfMistFactory.Create(_alice);

        // CR 702.3 — Defender wired as a KeywordAbility marker.
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "CR 702.3 — Defender is the only keyword on Wall of Mist");
        CombatAbilities.HasDefender(card).Should().BeTrue();
    }

    [Fact]
    public void WallOfMist_HasNoOtherKeywords()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().HaveCount(1,
                "Wall of Mist has exactly one keyword ability: Defender");
    }

    [Fact]
    public void WallOfMist_HasNoActivatedAbilities()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Wall of Mist is vanilla — no activated abilities");
    }

    [Fact]
    public void WallOfMist_HasNoTriggeredAbilities()
    {
        var card = WallOfMistFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Wall of Mist is vanilla — no triggered abilities beyond Defender marker");
    }

    // -----------------------------------------------------------------------
    // Dispatcher integration
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfMist_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Wall of Mist", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Wall of Mist");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().Contain(CardSubtype.Wall);

        var creature = card as Creature;
        creature.Should().NotBeNull();
        creature!.BasePower.Should().Be(0);
        creature.BaseToughness.Should().Be(5);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "dispatcher path attaches the Defender keyword");
    }
}
