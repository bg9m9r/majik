using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PlagueStingerFactory"/>
/// (Scars of Mirrodin, {1}{B}).
///
/// Creature — Phyrexian Insect 1/1. Oracle text:
///   "Flying.
///    Infect"
///
/// Covers:
///   - Identity (name, cost, P/T, plain Creature, subtypes
///     Phyrexian / Insect, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Flying + Infect keyword markers.
/// </summary>
public class PlagueStingerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PlagueStinger_Identity()
    {
        var c = PlagueStingerFactory.Create(_alice);

        c.Name.Should().Be("Plague Stinger");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeFalse(
            "Plague Stinger is a plain Creature, not an Artifact Creature");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PlagueStinger_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Plague Stinger", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Plague Stinger");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
    }

    [Fact]
    public void PlagueStinger_HasFlyingKeywordMarker()
    {
        var c = PlagueStingerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "Flying", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.9 — Flying keyword marker is wired");
    }

    [Fact]
    public void PlagueStinger_HasInfectKeywordMarker()
    {
        var c = PlagueStingerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "Infect", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.90 — Infect keyword marker is wired (mechanic deferred)");
    }

    [Fact]
    public void PlagueStinger_HasExactlyTwoKeywordMarkers()
    {
        var c = PlagueStingerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Flying + Infect — two keyword markers, no others");
    }
}
