using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MasterDecoyFactory"/> (Onslaught / 10th Edition,
/// {1}{W}). Creature — Human Soldier 1/2. Oracle text (verified against
/// Scryfall):
///   "{W}, {T}: Tap target creature."
///
/// Identical activated-ability shape to <see cref="GoldmeadowHarrierFactory"/>
/// (the same tap-target line); both are also the shape
/// <see cref="OracleActivatedAbilityBinder"/> reconstructs for Agatha's Soul
/// Cauldron's ability-grant.
///
/// Covers:
///   - Identity (1/2 Creature — Human Soldier, {1}{W}, owner / controller).
///   - The single activated ability + its cost shape ({W} + self-tap).
///   - Tap resolution taps the chosen creature (CR 701.21a).
///   - Tap resolution is a no-op on an off-battlefield target (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class MasterDecoyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MasterDecoy_Identity()
    {
        var decoy = MasterDecoyFactory.Create(_alice);

        decoy.Name.Should().Be("Master Decoy");
        decoy.ManaCost.Should().Be("{1}{W}");
        decoy.Power.Should().Be(1);
        decoy.Toughness.Should().Be(2);
        decoy.HasType(CardType.Creature).Should().BeTrue();
        decoy.HasSubtype(CardSubtype.Human).Should().BeTrue();
        decoy.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        decoy.Owner.Should().BeSameAs(_alice);
        decoy.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MasterDecoy_HasSingleTapActivatedAbility_WithManaAndTapCost()
    {
        var decoy = MasterDecoyFactory.Create(_alice);
        var activated = decoy.Abilities.OfType<ActivatedAbility>().ToList();

        activated.Should().ContainSingle("{W}, {T}: Tap target creature");

        var tap = activated.Single();

        // {W} mana pip — a single white requirement (TotalValue 1).
        tap.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue
            .Should().Be(1, "the {W} pip is a 1-mana requirement");

        // {T} — self-tap.
        tap.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);

        tap.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("tap target creature");
    }

    [Fact]
    public void MasterDecoy_TapAbility_TapsChosenCreature()
    {
        var decoy = MasterDecoyFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(decoy);
        decoy.SetZone(ZoneType.Battlefield);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var tap = decoy.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new[] { new object[] { grizzly } });

        grizzly.IsTapped.Should().BeFalse();
        tap.Resolve();
        grizzly.IsTapped.Should().BeTrue(
            "Fx.Tap delegates to Permanent.Tap (CR 701.21a)");
    }

    [Fact]
    public void MasterDecoy_TapAbility_NoOpOnNonBattlefieldTarget()
    {
        var decoy = MasterDecoyFactory.Create(_alice);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        // Deliberately NOT on the battlefield — CR 608.2b recheck rejects it.

        var tap = decoy.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new[] { new object[] { grizzly } });

        tap.Resolve();
        grizzly.IsTapped.Should().BeFalse(
            "CR 608.2b — target no longer on battlefield: effect fails silently");
    }
}
