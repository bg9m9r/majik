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
/// Unit tests for <see cref="LeadenMyrFactory"/> (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {B}."
///
/// Covers:
///   - Identity (name, cost, P/T, dual Artifact + Creature, Myr subtype,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - {T}: Add {B} mana ability — taps the myr, produces one black,
///     can't activate while already tapped.
/// </summary>
public class LeadenMyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void LeadenMyr_Identity()
    {
        var c = LeadenMyrFactory.Create(_alice);

        c.Name.Should().Be("Leaden Myr");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LeadenMyr_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Leaden Myr", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Leaden Myr");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {B} mana ability is attached");
    }

    // -------------------------------------------------------------------------
    // {T}: Add {B}
    // -------------------------------------------------------------------------

    [Fact]
    public void LeadenMyr_TapForBlack_TapsCreatureAndProducesOneBlack()
    {
        var c = LeadenMyrFactory.Create(_alice);
        // CR 302.6 — clear summoning sickness so this test exercises the
        // {T}: Add {B} mana production rather than the sickness gate.
        c.ClearSummoningSickness();

        var manaAbility = c.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue(
            "untapped myr — mana ability gate is open");
        var produced = manaAbility.Activate();

        produced.Black.Should().Be(1);
        produced.Generic.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        c.IsTapped.Should().BeTrue(
            "{T} cost tapped the myr as part of activation");
    }

    [Fact]
    public void LeadenMyr_ManaAbility_CannotActivateWhileTapped()
    {
        var c = LeadenMyrFactory.Create(_alice);
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
