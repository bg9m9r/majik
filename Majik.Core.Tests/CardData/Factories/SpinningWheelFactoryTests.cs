using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SpinningWheelFactory"/> (Time Spiral / Mirage, {3}).
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color.
///    {5}, {T}: Tap target creature."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity ({3} colourless Artifact).
///   - Five WUBRG mana-ability slots, each producing exactly one coloured mana
///     (CR 605.1a — "any color" modelled as five distinct ManaAbility slots,
///     the same shape as Manalith).
///   - The {5},{T} tap-target-creature activated ability + its cost shape
///     ({5} mana + self-tap) and that resolution taps the chosen creature
///     (CR 701.21a), mirroring Goldmeadow Harrier.
///
/// Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests, so they are not re-asserted here.
/// </summary>
[Trait("Color", "C")]
public class SpinningWheelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SpinningWheel_Identity()
    {
        var wheel = SpinningWheelFactory.Create(_alice);

        wheel.Name.Should().Be("Spinning Wheel");
        wheel.ManaCost.Should().Be("{3}");
        wheel.HasType(CardType.Artifact).Should().BeTrue();
        wheel.Owner.Should().BeSameAs(_alice);
        wheel.Controller.Should().BeSameAs(_alice);
    }

    // --------------------------------------------------------------
    // {T}: Add one mana of any color — five single-colour mana abilities.
    // --------------------------------------------------------------

    [Fact]
    public void SpinningWheel_HasFiveManaAbilities_OnePerColor()
    {
        var wheel = SpinningWheelFactory.Create(_alice);

        var manaAbilities = wheel.Abilities.OfType<ManaAbility>().ToList();
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

    // --------------------------------------------------------------
    // {5}, {T}: Tap target creature — activated ability.
    // --------------------------------------------------------------

    [Fact]
    public void SpinningWheel_HasTapActivatedAbility_WithFiveManaAndSelfTapCost()
    {
        var wheel = SpinningWheelFactory.Create(_alice);
        var activated = wheel.Abilities.OfType<ActivatedAbility>().ToList();

        activated.Should().ContainSingle("{5}, {T}: Tap target creature");

        var tap = activated.Single();

        // {5} generic pip — a 5-mana requirement (TotalValue 5).
        tap.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue
            .Should().Be(5, "the {5} pip is a 5-mana requirement");

        // {T} — self-tap.
        tap.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);

        tap.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("tap target creature");
    }

    [Fact]
    public void SpinningWheel_TapAbility_TapsChosenCreature()
    {
        var wheel = SpinningWheelFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wheel);
        wheel.SetZone(ZoneType.Battlefield);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var tap = wheel.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new[] { new object[] { grizzly } });

        grizzly.IsTapped.Should().BeFalse();
        // The declarative tap_target effect reads its chosen target off the
        // resolving ability's ChosenTargets via the ResolutionContext, so it
        // must be driven through Resolve() (the targeted-effect path).
        tap.Resolve();
        grizzly.IsTapped.Should().BeTrue(
            "Fx.Tap delegates to Permanent.Tap (CR 701.21a)");
    }

    [Fact]
    public void SpinningWheel_TapAbility_NoOpOnNonBattlefieldTarget()
    {
        var wheel = SpinningWheelFactory.Create(_alice);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        // Deliberately NOT on the battlefield — CR 608.2b recheck rejects it.

        var tap = wheel.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new[] { new object[] { grizzly } });

        tap.Resolve();
        grizzly.IsTapped.Should().BeFalse(
            "CR 608.2b — target no longer on battlefield: effect fails silently");
    }
}
