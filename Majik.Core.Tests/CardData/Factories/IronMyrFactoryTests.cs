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
/// Unit tests for <see cref="IronMyrFactory"/>
/// (Mirrodin Besieged, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {R}."
///
/// Covers:
///   - Identity (name, cost {2}, P/T 1/1, dual Artifact + Creature, Myr
///     subtype, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - {T}: Add {R} mana ability — taps the myr, produces one red pip,
///     can't activate while already tapped.
/// </summary>
public class IronMyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void IronMyr_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Iron Myr", _alice);

        c.Name.Should().Be("Iron Myr");
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
    public void IronMyr_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Iron Myr", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Iron Myr");
        c.HasType(CardType.Artifact).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {R} mana ability is attached");
    }

    // -------------------------------------------------------------------------
    // {T}: Add {R}
    // -------------------------------------------------------------------------

    [Fact]
    public void IronMyr_TapForRed_TapsCreatureAndProducesOneRed()
    {
        var c = (Creature)NamedCardFactory.Create("Iron Myr", _alice);
        // CR 302.6 — clear summoning sickness so this test exercises the
        // {T}: Add {R} mana production rather than the sickness gate.
        c.ClearSummoningSickness();

        var manaAbility = c.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue(
            "untapped myr — mana ability gate is open");
        var produced = manaAbility.Activate();

        produced.Red.Should().Be(1);
        produced.Generic.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Green.Should().Be(0);
        c.IsTapped.Should().BeTrue(
            "{T} cost tapped the myr as part of activation");
    }

    [Fact]
    public void IronMyr_ManaAbility_CannotActivateWhileTapped()
    {
        var c = (Creature)NamedCardFactory.Create("Iron Myr", _alice);
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
