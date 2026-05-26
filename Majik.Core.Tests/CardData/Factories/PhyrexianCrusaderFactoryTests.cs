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
/// Unit tests for <see cref="PhyrexianCrusaderFactory"/>
/// (Mirrodin Besieged, {1}{B}{B}).
///
/// Creature — Phyrexian Knight 2/2. Oracle text:
///   "First strike.
///    Protection from red and from white.
///    Infect"
///
/// Covers:
///   - Identity (name, cost, P/T, subtypes Phyrexian / Knight,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - First strike keyword marker readable by
///     <see cref="CombatAbilities.HasFirstStrike"/>.
///   - Two independent <see cref="ProtectionAbility"/> instances
///     (qualities "red" and "white"), each with no IsActive gate
///     (always-on, not conditional like Etched Champion's Metalcraft).
///   - <see cref="Protection.HasProtectionFromColor"/> answers true for
///     Red + White and false for Blue / Black / Green.
///   - Infect keyword marker is attached.
/// </summary>
public class PhyrexianCrusaderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void PhyrexianCrusader_Identity()
    {
        var c = PhyrexianCrusaderFactory.Create(_alice);

        c.Name.Should().Be("Phyrexian Crusader");
        c.ManaCost.Should().Be("{1}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeFalse(
            "Phyrexian Crusader is a plain Creature, not an Artifact Creature");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PhyrexianCrusader_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Phyrexian Crusader", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Phyrexian Crusader");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2,
            "Protection from red AND protection from white — two qualities");
    }

    // -------------------------------------------------------------------------
    // First strike
    // -------------------------------------------------------------------------

    [Fact]
    public void PhyrexianCrusader_HasFirstStrike()
    {
        var c = PhyrexianCrusaderFactory.Create(_alice);

        CombatAbilities.HasFirstStrike(c).Should().BeTrue(
            "CR 702.7 — First strike keyword marker is wired");
        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "First strike", System.StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // Protection — independent Red + White qualities
    // -------------------------------------------------------------------------

    [Fact]
    public void PhyrexianCrusader_HasIndependentProtectionFromRedAndWhite()
    {
        var c = PhyrexianCrusaderFactory.Create(_alice);

        var protections = c.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .OrderBy(q => q)
            .ToList();

        protections.Should().BeEquivalentTo(new[] { "red", "white" },
            "two distinct qualities — Sword of Fire and Ice-style pair, " +
            "not a single combined string");

        // Both protections are always-on (no IsActive gate — this isn't
        // Etched Champion's conditional Metalcraft rider).
        foreach (var prot in c.Abilities.OfType<ProtectionAbility>())
        {
            prot.IsActive.Should().BeNull(
                $"Protection from {prot.Quality} is unconditional — no gate");
            prot.IsCurrentlyActive.Should().BeTrue();
        }
    }

    [Fact]
    public void PhyrexianCrusader_HasProtectionFromColor_RedAndWhite_True()
    {
        var c = PhyrexianCrusaderFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeTrue(
            "Protection from red — CR 702.16");
        Protection.HasProtectionFromColor(c, ManaColor.White).Should().BeTrue(
            "Protection from white — CR 702.16");
    }

    [Fact]
    public void PhyrexianCrusader_HasProtectionFromColor_OtherColors_False()
    {
        var c = PhyrexianCrusaderFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Black).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Green).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Infect
    // -------------------------------------------------------------------------

    [Fact]
    public void PhyrexianCrusader_HasInfectKeywordMarker()
    {
        var c = PhyrexianCrusaderFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "Infect", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.90 — Infect keyword marker is wired (mechanic deferred)");
    }
}
