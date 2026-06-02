using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SphinxOfTheSteelWindFactory"/>.
///
/// Sphinx of the Steel Wind (Alara Reborn, {5}{W}{U}{B}). Artifact Creature —
/// Sphinx 6/6. Oracle text (verified against Scryfall):
///   "Flying, first strike, vigilance, lifelink, protection from red and from green"
///
/// Coverage:
/// - Identity (name, Artifact + Creature types, Sphinx subtype, cost, colours,
///   P/T, owner/controller).
/// - NamedCardFactory dispatch.
/// - Flying / first strike / vigilance / lifelink keyword markers
///   (CR 702.9 / 702.7 / 702.21 / 702.15) surfaced via CombatAbilities.
/// - Protection from red and from green (CR 702.16) — both quality markers
///   present, surfaced via Rules.Protection.HasProtectionFromColor.
/// - No protection from other colours.
/// </summary>
[Trait("Color", "WUB")]
public class SphinxOfTheSteelWindFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void SphinxOfTheSteelWind_Identity()
    {
        var c = SphinxOfTheSteelWindFactory.Create(_alice);

        c.Name.Should().Be("Sphinx of the Steel Wind");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Sphinx).Should().BeTrue();
        c.ManaCost.Should().Be("{5}{W}{U}{B}");
        c.ManaCostValue.TotalValue.Should().Be(8);
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(6);
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().Contain(ManaColor.Black);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SphinxOfTheSteelWind_DispatchesViaNamedFactory()
    {
        var card = NamedCardFactory.Create("Sphinx of the Steel Wind", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sphinx of the Steel Wind");
    }

    // ── Evergreen combat keywords ───────────────────────────────────────

    [Fact]
    public void SphinxOfTheSteelWind_HasFlyingFirstStrikeVigilanceLifelink()
    {
        var c = SphinxOfTheSteelWindFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue(
            "Sphinx of the Steel Wind prints Flying (CR 702.9).");
        CombatAbilities.HasFirstStrike(c).Should().BeTrue(
            "Sphinx of the Steel Wind prints first strike (CR 702.7).");
        CombatAbilities.HasVigilance(c).Should().BeTrue(
            "Sphinx of the Steel Wind prints vigilance (CR 702.21).");
        CombatAbilities.HasLifelink(c).Should().BeTrue(
            "Sphinx of the Steel Wind prints lifelink (CR 702.15).");
    }

    // ── Protection ──────────────────────────────────────────────────────

    [Fact]
    public void SphinxOfTheSteelWind_HasProtectionFromRedAndGreen()
    {
        var c = SphinxOfTheSteelWindFactory.Create(_alice);

        var qualities = c.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();
        qualities.Should().BeEquivalentTo(new[] { "red", "green" },
            "CR 702.16 — protection from red and from green are the printed riders.");
    }

    [Fact]
    public void SphinxOfTheSteelWind_ProtectionFromColor_SurfacesViaRulesProtection()
    {
        var c = SphinxOfTheSteelWindFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeTrue(
            "Sphinx of the Steel Wind has protection from red (CR 702.16).");
        Protection.HasProtectionFromColor(c, ManaColor.Green).Should().BeTrue(
            "Sphinx of the Steel Wind has protection from green (CR 702.16).");
    }

    [Fact]
    public void SphinxOfTheSteelWind_NoProtectionFromOtherColors()
    {
        var c = SphinxOfTheSteelWindFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.White).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Black).Should().BeFalse();
    }
}
