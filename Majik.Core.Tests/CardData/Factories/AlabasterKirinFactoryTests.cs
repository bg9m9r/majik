using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AlabasterKirinFactory"/>.
///
/// Card: Alabaster Kirin — {3}{W} Creature — Kirin 2/3.
///   "Flying, vigilance"
///
/// Contract plumbing (production dispatch + well-formedness) is covered for
/// every implemented card by <c>CardFactoryContractTests</c>; these tests
/// assert only Alabaster Kirin's identity + its UNIQUE keyword markers.
/// </summary>
[Trait("Color", "W")]
public class AlabasterKirinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AlabasterKirin_Identity()
    {
        var c = AlabasterKirinFactory.Create(_alice);

        c.Name.Should().Be("Alabaster Kirin");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Kirin).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);
        // {3}{W} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
    }

    [Fact]
    public void AlabasterKirin_HasFlyingAndVigilanceMarkers()
    {
        var c = AlabasterKirinFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();

        // CR 702.9 / CR 702.20 — both printed keywords present as markers.
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Vigilance");
        keywords.Should().HaveCount(2, "Flying and Vigilance are the only printed keywords");
    }

    [Fact]
    public void AlabasterKirin_HasNoTriggeredOrActivatedAbilities()
    {
        var c = AlabasterKirinFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
