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
/// Unit tests for <see cref="GoldMyrFactory"/> (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {W}."
///
/// Covers:
///   - Identity (name, cost {2}, P/T 1/1, dual Artifact + Creature, Myr
///     subtype, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - {T}: Add {W} mana ability — taps the myr, produces one white pip,
///     can't activate while already tapped.
/// </summary>
public class GoldMyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void GoldMyr_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Gold Myr", _alice);

        c.Name.Should().Be("Gold Myr");
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
    public void GoldMyr_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Gold Myr", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Gold Myr");
        c.HasType(CardType.Artifact).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {W} mana ability is attached");
    }

    // -------------------------------------------------------------------------
    // {T}: Add {W}
    // -------------------------------------------------------------------------

    [Fact]
    public void GoldMyr_TapForWhite_TapsCreatureAndProducesOneWhite()
    {
        var c = (Creature)NamedCardFactory.Create("Gold Myr", _alice);
        // CR 302.6 — clear summoning sickness so this test exercises the
        // {T}: Add {W} mana production rather than the sickness gate.
        c.ClearSummoningSickness();

        var manaAbility = c.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue(
            "untapped myr — mana ability gate is open");
        var produced = manaAbility.Activate();

        produced.White.Should().Be(1);
        produced.Generic.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        c.IsTapped.Should().BeTrue(
            "{T} cost tapped the myr as part of activation");
    }

    [Fact]
    public void GoldMyr_ManaAbility_CannotActivateWhileTapped()
    {
        var c = (Creature)NamedCardFactory.Create("Gold Myr", _alice);
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
