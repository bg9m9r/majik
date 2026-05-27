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
/// Unit tests for <see cref="PlagueMyrFactory"/>
/// (Mirrodin Besieged, {2}).
///
/// Artifact Creature — Phyrexian Myr 1/1. Oracle text:
///   "Infect
///    {T}: Add {C}."
///
/// Covers:
///   - Identity (name, cost, P/T, dual Artifact + Creature, subtypes
///     Phyrexian / Myr, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Infect keyword marker.
///   - {T}: Add {C} mana ability — taps the myr, produces one
///     colourless (bucketed as +1 generic per
///     <see cref="ValueObjects.ManaCost.Parse"/>), can't activate while
///     already tapped.
/// </summary>
public class PlagueMyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void PlagueMyr_Identity()
    {
        var c = PlagueMyrFactory.Create(_alice);

        c.Name.Should().Be("Plague Myr");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PlagueMyr_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Plague Myr", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Plague Myr");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} mana ability is attached");
        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "Infect", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.90 — Infect keyword marker is wired");
    }

    // -------------------------------------------------------------------------
    // Infect
    // -------------------------------------------------------------------------

    [Fact]
    public void PlagueMyr_HasInfectKeywordMarker()
    {
        var c = PlagueMyrFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().Contain(k =>
            string.Equals(k.Keyword, "Infect", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.90 — Infect keyword marker is wired (mechanic deferred)");
    }

    // -------------------------------------------------------------------------
    // {T}: Add {C}
    // -------------------------------------------------------------------------

    [Fact]
    public void PlagueMyr_TapForColorless_TapsCreatureAndProducesOneGeneric()
    {
        var c = PlagueMyrFactory.Create(_alice);
        // CR 302.6 — clear summoning sickness so this test exercises the
        // {T}: Add {C} mana production rather than the sickness gate.
        c.ClearSummoningSickness();

        var manaAbility = c.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue(
            "untapped myr — mana ability gate is open");
        var produced = manaAbility.Activate();

        // {C} is bucketed as +1 generic in ValueObjects.ManaCost today
        // (same convention as Inkmoth Nexus' {T}: Add {C}).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        c.IsTapped.Should().BeTrue(
            "{T} cost tapped the myr as part of activation");
    }

    [Fact]
    public void PlagueMyr_ManaAbility_CannotActivateWhileTapped()
    {
        var c = PlagueMyrFactory.Create(_alice);
        // CR 302.6 — clear summoning sickness so the first activation is legal
        // and the test asserts the !IsTapped re-activation gate specifically.
        c.ClearSummoningSickness();

        var manaAbility = c.Abilities.OfType<ManaAbility>().Single();

        manaAbility.Activate();
        c.IsTapped.Should().BeTrue();

        manaAbility.CanActivate().Should().BeFalse(
            "tapped myr — mana ability !IsTapped gate is closed");
    }
}
