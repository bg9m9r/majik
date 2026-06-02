using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WallOfSpearsFactory"/>.
///
/// Card: Wall of Spears — Artifact Creature — Wall {3} 2/3 (Alpha/Beta/…).
/// Oracle text:
///   "Defender.
///    First strike."
///
/// Covers:
///   - Identity (name, cost {3}, P/T 2/3, Artifact + Creature types,
///     subtype Wall, colourless — no colours in mana cost, mana value 3,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Defender keyword marker readable by combat helpers.
///   - First strike keyword marker readable by
///     <see cref="CombatAbilities.HasFirstStrike"/>.
///   - Exactly two KeywordAbility instances (Defender + First strike).
/// </summary>
[Trait("Color", "C")]
public class WallOfSpearsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void WallOfSpears_Identity()
    {
        var c = WallOfSpearsFactory.Create(_alice);

        c.Name.Should().Be("Wall of Spears");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue("Wall of Spears is a Creature");
        c.HasType(CardType.Artifact).Should().BeTrue(
            "CR 301.1 / 302.1 — Wall of Spears is an Artifact Creature");
        c.HasSubtype(CardSubtype.Wall).Should().BeTrue(
            "subtype Wall (CR 205.3m)");
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WallOfSpears_IsColourless_ManaValueThree()
    {
        var c = WallOfSpearsFactory.Create(_alice);

        // Generic-only mana cost means no colour identity (CR 202.2).
        CardColors.GetColors(c).Should().BeEmpty(
            "Wall of Spears has a generic-only mana cost — it is colourless");
        c.ManaCostValue.TotalValue.Should().Be(3,
            "mana value equals the sum of mana symbols: one generic {3}");
    }

    // -------------------------------------------------------------------------
    // Dispatch
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // Keywords
    // -------------------------------------------------------------------------

    [Fact]
    public void WallOfSpears_HasDefenderKeywordMarker()
    {
        var c = WallOfSpearsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "Defender", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.3 — Defender keyword marker must be wired");
    }

    [Fact]
    public void WallOfSpears_HasFirstStrikeKeywordMarker()
    {
        var c = WallOfSpearsFactory.Create(_alice);

        CombatAbilities.HasFirstStrike(c).Should().BeTrue(
            "CR 702.7 — First strike keyword marker is wired");
        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "First strike", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WallOfSpears_HasExactlyTwoKeywords_DefenderAndFirstStrike()
    {
        var c = WallOfSpearsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Wall of Spears has exactly two printed keywords: Defender and First strike");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Wall of Spears has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Wall of Spears has no activated abilities");
    }
}
