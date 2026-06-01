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
/// Unit tests for <see cref="PalladiumMyrFactory"/>
/// (Scars of Mirrodin, {4}).
///
/// Artifact Creature — Myr 2/2. Oracle text (verified against Scryfall):
///   "{T}: Add {C}{C}."
///
/// Covers:
///   - Identity (name, cost {4}, P/T 2/2, dual Artifact + Creature, Myr
///     subtype, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - {T}: Add {C}{C} mana ability — taps the myr, produces two
///     colourless pips together (bucketed as +2 generic per
///     <see cref="ValueObjects.ManaCost.Parse"/>), can't activate while
///     already tapped.
/// </summary>
public class PalladiumMyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void PalladiumMyr_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Palladium Myr", _alice);

        c.Name.Should().Be("Palladium Myr");
        c.ManaCost.Should().Be("{4}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PalladiumMyr_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Palladium Myr", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Palladium Myr");
        c.HasType(CardType.Artifact).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C}{C} mana ability is attached");
    }

    // -------------------------------------------------------------------------
    // {T}: Add {C}{C}
    // -------------------------------------------------------------------------

    [Fact]
    public void PalladiumMyr_TapForColorless_TapsCreatureAndProducesTwoGeneric()
    {
        var c = (Creature)NamedCardFactory.Create("Palladium Myr", _alice);
        // CR 302.6 — clear summoning sickness so this test exercises the
        // {T}: Add {C}{C} mana production rather than the sickness gate.
        c.ClearSummoningSickness();

        var manaAbility = c.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue(
            "untapped myr — mana ability gate is open");
        var produced = manaAbility.Activate();

        // Each {C} is bucketed as +1 generic in ValueObjects.ManaCost today
        // (CR 107.4c — no dedicated colourless bucket; same convention as
        // Plague Myr / Mind Stone). Two pips emitted together → +2 generic.
        produced.Generic.Should().Be(2);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        c.IsTapped.Should().BeTrue(
            "{T} cost tapped the myr as part of activation");
    }

    [Fact]
    public void PalladiumMyr_ManaAbility_CannotActivateWhileTapped()
    {
        var c = (Creature)NamedCardFactory.Create("Palladium Myr", _alice);
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
