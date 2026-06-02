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
/// Unit tests for <see cref="ChildOfNightFactory"/>.
///
/// Covers:
/// - Identity ({1}{B} Creature — Vampire, 2/1, black).
/// - Lifelink keyword marker (CR 702.15).
/// - Mana value 2 (CR 202.3).
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "B")]
public class ChildOfNightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ChildOfNight_Identity()
    {
        var c = ChildOfNightFactory.Create(_alice);

        c.Name.Should().Be("Child of Night");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue("Child of Night is a Vampire");
        c.ManaCost.Should().Be("{1}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ChildOfNight_IsBlack()
    {
        var c = ChildOfNightFactory.Create(_alice);
        // Color is derived from mana cost — {B} pip makes it black.
        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Black,
            "Child of Night has a {B} pip in its mana cost");
        colors.Should().HaveCount(1, "only one color");
    }

    [Fact]
    public void ChildOfNight_ManaValue_IsTwo()
    {
        var c = ChildOfNightFactory.Create(_alice);
        // {1}{B} = mana value 2 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {1}{B} has mana value 2");
    }

    // -----------------------------------------------------------------------
    // Keyword markers
    // -----------------------------------------------------------------------

    [Fact]
    public void ChildOfNight_HasLifelinkKeyword()
    {
        var c = ChildOfNightFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Lifelink",
                "CR 702.15 — Child of Night has Lifelink");
    }

    [Fact]
    public void ChildOfNight_HasExactlyOneKeywordAbility()
    {
        var c = ChildOfNightFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().HaveCount(1, "Child of Night has only Lifelink — no other keyword abilities");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
}
