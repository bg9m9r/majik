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
/// Tests for <see cref="GoldmeadowHarrierFactory"/> (Lorwyn / 10th Edition,
/// {W}). Creature — Kithkin Soldier 1/1. Oracle text (verified against
/// Scryfall):
///   "{W}, {T}: Tap target creature."
///
/// Covers:
///   - Identity (1/1 Creature — Kithkin Soldier, {W}, owner / controller).
///   - NamedCardFactory dispatch.
///   - The single activated ability + its cost shape ({W} + self-tap).
///   - Tap resolution taps the chosen creature (CR 701.21).
///   - Tap resolution is a no-op on an off-battlefield target (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class GoldmeadowHarrierFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GoldmeadowHarrier_Identity()
    {
        var harrier = GoldmeadowHarrierFactory.Create(_alice);

        harrier.Name.Should().Be("Goldmeadow Harrier");
        harrier.ManaCost.Should().Be("{W}");
        harrier.Power.Should().Be(1);
        harrier.Toughness.Should().Be(1);
        harrier.HasType(CardType.Creature).Should().BeTrue();
        harrier.HasSubtype(CardSubtype.Kithkin).Should().BeTrue();
        harrier.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        harrier.Owner.Should().BeSameAs(_alice);
        harrier.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void GoldmeadowHarrier_HasSingleTapActivatedAbility_WithManaAndTapCost()
    {
        var harrier = GoldmeadowHarrierFactory.Create(_alice);
        var activated = harrier.Abilities.OfType<ActivatedAbility>().ToList();

        activated.Should().ContainSingle("{W}, {T}: Tap target creature");

        var tap = activated.Single();

        // {W} mana pip — a single white requirement (TotalValue 1).
        tap.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue
            .Should().Be(1, "the {W} pip is a 1-mana requirement");

        // {T} — self-tap.
        tap.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);

        tap.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("target creature");
    }

    [Fact]
    public void GoldmeadowHarrier_TapAbility_TapsChosenCreature()
    {
        var harrier = GoldmeadowHarrierFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(harrier);
        harrier.SetZone(ZoneType.Battlefield);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var tap = harrier.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new[] { new object[] { grizzly } });

        grizzly.IsTapped.Should().BeFalse();
        foreach (var effect in tap.Effects) effect.Execute();
        grizzly.IsTapped.Should().BeTrue(
            "Fx.Tap delegates to Permanent.Tap (CR 701.21)");
    }

    [Fact]
    public void GoldmeadowHarrier_TapAbility_NoOpOnNonBattlefieldTarget()
    {
        var harrier = GoldmeadowHarrierFactory.Create(_alice);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        // Deliberately NOT on the battlefield — CR 608.2b recheck rejects it.

        var tap = harrier.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new[] { new object[] { grizzly } });

        foreach (var effect in tap.Effects) effect.Execute();
        grizzly.IsTapped.Should().BeFalse(
            "CR 608.2b — target no longer on battlefield: effect fails silently");
    }
}
