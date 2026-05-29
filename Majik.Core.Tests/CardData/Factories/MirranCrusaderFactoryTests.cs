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
/// Unit tests for <see cref="MirranCrusaderFactory"/>.
///
/// Mirran Crusader (Mirrodin Besieged, {1}{W}{W}). Creature — Human Knight
/// 2/2. Oracle text (verified against Scryfall):
///   "Double strike, protection from black and from green"
///
/// Coverage:
/// - Identity (name, type, Human + Knight subtypes, cost, colour, P/T,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Double strike keyword marker (CR 702.4) surfaced via CombatAbilities.
/// - Protection from black and from green (CR 702.16) — both quality
///   markers present, surfaced via Rules.Protection.HasProtectionFromColor.
/// - No protection from other colours.
/// </summary>
public class MirranCrusaderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void MirranCrusader_Identity()
    {
        var c = MirranCrusaderFactory.Create(_alice);

        c.Name.Should().Be("Mirran Crusader");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{W}{W}");
        c.ManaCostValue.TotalValue.Should().Be(3);
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.White);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MirranCrusader_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Mirran Crusader", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Mirran Crusader");
        ((Creature)c).HasSubtype(CardSubtype.Human).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
    }

    // ── Double strike ───────────────────────────────────────────────────

    [Fact]
    public void MirranCrusader_HasDoubleStrike()
    {
        var c = MirranCrusaderFactory.Create(_alice);

        CombatAbilities.HasDoubleStrike(c).Should().BeTrue(
            "Mirran Crusader prints Double strike (CR 702.4).");
    }

    // ── Protection ──────────────────────────────────────────────────────

    [Fact]
    public void MirranCrusader_HasProtectionFromBlackAndGreen()
    {
        var c = MirranCrusaderFactory.Create(_alice);

        var qualities = c.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();
        qualities.Should().BeEquivalentTo(new[] { "black", "green" },
            "CR 702.16 — protection from black and from green are the printed riders.");
    }

    [Fact]
    public void MirranCrusader_ProtectionFromColor_SurfacesViaRulesProtection()
    {
        var c = MirranCrusaderFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.Black).Should().BeTrue(
            "Mirran Crusader has protection from black (CR 702.16).");
        Protection.HasProtectionFromColor(c, ManaColor.Green).Should().BeTrue(
            "Mirran Crusader has protection from green (CR 702.16).");
    }

    [Fact]
    public void MirranCrusader_NoProtectionFromOtherColors()
    {
        var c = MirranCrusaderFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.White).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeFalse();
    }
}
