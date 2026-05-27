using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WallOfSwordsFactory"/> (Fifth Edition and
/// reprints, {3}{W}).
///
/// Creature — Wall 3/5. Oracle text:
///   "Defender.
///    Flying."
///
/// Covers:
/// - Card identity (3/5 Creature — Wall, mana cost {3}{W}, MV 4).
/// - White colour identity via <see cref="CardColors.GetColors"/>.
/// - Defender keyword marker (CR 702.3) surfaced via
///   <see cref="CombatAbilities.HasDefender"/>.
/// - Flying keyword marker (CR 702.9) surfaced via
///   <see cref="CombatAbilities.HasFlying"/>.
/// - No activated abilities or triggered abilities (vanilla keywords only).
/// - NamedCardFactory dispatcher resolves "Wall of Swords" to the
///   expected shape.
/// </summary>
public class WallOfSwordsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfSwords_IsCreature()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void WallOfSwords_NameIsCorrect()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Name.Should().Be("Wall of Swords");
    }

    [Fact]
    public void WallOfSwords_HasCorrectPrintedManaCost()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.ManaCost.Should().Be("{3}{W}");
    }

    [Fact]
    public void WallOfSwords_HasCorrectPrintedPowerAndToughness()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(5);
    }

    [Fact]
    public void WallOfSwords_ManaValueIsFour()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(4,
            "{3}{W} = 3 generic + 1 white = MV 4 (CR 202.3)");
    }

    [Fact]
    public void WallOfSwords_HasWallSubtype()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Subtypes.Should().Contain(CardSubtype.Wall,
            "Wall of Swords is a Creature — Wall");
    }

    [Fact]
    public void WallOfSwords_HasOnlyWallSubtype()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Subtypes.Should().HaveCount(1,
            "the only printed subtype is Wall");
    }

    [Fact]
    public void WallOfSwords_OwnerAndControllerAreSet()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Colour identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfSwords_IsWhite()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        var colors = CardColors.GetColors(card);

        colors.Should().Contain(ManaColor.White,
            "{W} pip in {3}{W} makes this a white card");
        colors.Should().HaveCount(1,
            "Wall of Swords is mono-white");
    }

    // -----------------------------------------------------------------------
    // Defender keyword (CR 702.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfSwords_HasDefenderKeyword()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "CR 702.3 — Defender wired as a KeywordAbility marker");
        CombatAbilities.HasDefender(card).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Flying keyword (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfSwords_HasFlyingKeyword()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "CR 702.9 — Flying wired as a KeywordAbility marker");
        CombatAbilities.HasFlying(card).Should().BeTrue();
    }

    [Fact]
    public void WallOfSwords_CanBlockFlying()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        // Flying creatures can block other flying creatures (CR 702.9b).
        CombatAbilities.CanBlockFlying(card).Should().BeTrue(
            "a Flying permanent satisfies the can-block-flying check");
    }

    // -----------------------------------------------------------------------
    // No activated / triggered abilities (vanilla keywords only)
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfSwords_HasNoActivatedAbilities()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Wall of Swords has no activated abilities");
    }

    [Fact]
    public void WallOfSwords_HasNoTriggeredAbilities()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Wall of Swords has no triggered abilities");
    }

    [Fact]
    public void WallOfSwords_HasExactlyTwoKeywordAbilities()
    {
        var card = WallOfSwordsFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "exactly Defender + Flying, no other keywords");
    }

    // -----------------------------------------------------------------------
    // Dispatcher integration
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfSwords_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Wall of Swords", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Wall of Swords");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().Contain(CardSubtype.Wall);
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "dispatcher path attaches the Defender keyword");
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "dispatcher path attaches the Flying keyword");
    }
}
