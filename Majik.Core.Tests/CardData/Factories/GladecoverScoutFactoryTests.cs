using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GladecoverScoutFactory"/> (Innistrad, {G}).
///
/// Creature — Elf Scout 1/1. Oracle text (verified against Scryfall):
///   "Hexproof (This creature can't be the target of spells or abilities
///    your opponents control.)"
///
/// Covers:
///   - Identity (name, cost, P/T, subtypes Elf / Scout, owner / controller).
///   - Hexproof keyword marker attached unconditionally (CR 702.11) — the
///     live read path for <see cref="Majik.Core.Targeting.TargetLegality"/>.
///   - No stray abilities beyond the single Hexproof marker.
/// </summary>
[Trait("Color", "G")]
public class GladecoverScoutFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GladecoverScout_Identity()
    {
        var c = GladecoverScoutFactory.Create(_alice);

        c.Name.Should().Be("Gladecover Scout");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GladecoverScout_HasHexproofKeywordMarker()
    {
        var c = GladecoverScoutFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Hexproof",
            "card-text marker for the Hexproof keyword (CR 702.11) — read by " +
            "TargetLegality to reject opponents' targeting (CR 702.11b)");
    }

    [Fact]
    public void GladecoverScout_HasExactlyOneAbility_HexproofOnly()
    {
        var c = GladecoverScoutFactory.Create(_alice);

        c.Abilities.Should().ContainSingle(
            "Gladecover Scout is a near-vanilla 1/1 — its only rider is Hexproof");
        c.Abilities.OfType<KeywordAbility>().Should().ContainSingle()
            .Which.Keyword.Should().Be("Hexproof");
    }
}
