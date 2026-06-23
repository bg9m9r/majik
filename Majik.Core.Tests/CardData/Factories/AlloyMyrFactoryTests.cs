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
/// Unit tests for <see cref="AlloyMyrFactory"/> (Scars of Mirrodin, {3}).
///
/// Artifact Creature — Myr 2/2. Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color."
///
/// Covers:
///   - Identity (name, cost {3}, P/T 2/2, dual Artifact + Creature, Myr
///     subtype) — the non-vanilla stats that the contract test does not pin.
///   - "{T}: Add one mana of any color" — five WUBRG ManaAbility slots
///     (CR 605.1a), each producing exactly one coloured pip and tapping the myr.
/// </summary>
[Trait("Color", "C")]
public class AlloyMyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity (non-vanilla stats)
    // -------------------------------------------------------------------------

    [Fact]
    public void AlloyMyr_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Alloy Myr", _alice);

        c.Name.Should().Be("Alloy Myr");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------------
    // {T}: Add one mana of any color — five WUBRG slots (CR 605.1a)
    // -------------------------------------------------------------------------

    [Fact]
    public void AlloyMyr_HasFiveManaAbilities_OnePerColor()
    {
        var c = (Creature)NamedCardFactory.Create("Alloy Myr", _alice);

        var manaAbilities = c.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5);

        // Each slot produces exactly one mana (one of W/U/B/R/G).
        manaAbilities.Should().OnlyContain(ma => ma.ManaGenerated.TotalValue == 1);

        var produced = manaAbilities.Select(ma => ma.ManaGenerated).ToList();
        produced.Should().ContainSingle(m => m.White == 1);
        produced.Should().ContainSingle(m => m.Blue == 1);
        produced.Should().ContainSingle(m => m.Black == 1);
        produced.Should().ContainSingle(m => m.Red == 1);
        produced.Should().ContainSingle(m => m.Green == 1);
    }

    [Fact]
    public void AlloyMyr_TapForColor_TapsCreatureAndProducesOneMana()
    {
        var c = (Creature)NamedCardFactory.Create("Alloy Myr", _alice);
        // CR 302.6 — clear summoning sickness so this test exercises the
        // mana production rather than the sickness gate.
        c.ClearSummoningSickness();

        // Pick the green slot to prove an arbitrary colour taps + produces.
        var greenAbility = c.Abilities.OfType<ManaAbility>()
            .Single(ma => ma.ManaGenerated.Green == 1);

        greenAbility.CanActivate().Should().BeTrue(
            "untapped myr — mana ability gate is open");
        var produced = greenAbility.Activate();

        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);
        c.IsTapped.Should().BeTrue(
            "{T} cost tapped the myr as part of activation");
    }
}
