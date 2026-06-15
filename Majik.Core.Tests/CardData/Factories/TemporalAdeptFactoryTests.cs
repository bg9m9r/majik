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
/// Tests for <see cref="TemporalAdeptFactory"/> (Urza's Saga / 8th–10th Edition,
/// {1}{U}{U}). Creature — Human Wizard 1/1. Oracle text (verified against
/// Scryfall):
///   "{U}{U}{U}, {T}: Return target permanent to its owner's hand."
///
/// The return-to-hand activated shape that
/// <see cref="OracleActivatedAbilityBinder"/> also reconstructs for Agatha's Soul
/// Cauldron's ability-grant (CR 613.1f / 702.49).
///
/// Covers:
///   - Identity (1/1 Creature — Human Wizard, {1}{U}{U}, owner / controller).
///   - The single activated ability + its cost shape ({U}{U}{U} + self-tap).
///   - Bounce resolution returns the chosen permanent to ITS OWNER's hand
///     (CR 701.20) — never the controller's.
///   - Bounce resolution is a no-op on an off-battlefield target (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class TemporalAdeptFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TemporalAdept_Identity()
    {
        var adept = TemporalAdeptFactory.Create(_alice);

        adept.Name.Should().Be("Temporal Adept");
        adept.ManaCost.Should().Be("{1}{U}{U}");
        adept.Power.Should().Be(1);
        adept.Toughness.Should().Be(1);
        adept.HasType(CardType.Creature).Should().BeTrue();
        adept.HasSubtype(CardSubtype.Human).Should().BeTrue();
        adept.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        adept.Owner.Should().BeSameAs(_alice);
        adept.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TemporalAdept_HasSingleReturnToHandAbility_WithManaAndTapCost()
    {
        var adept = TemporalAdeptFactory.Create(_alice);
        var activated = adept.Abilities.OfType<ActivatedAbility>().ToList();

        activated.Should().ContainSingle(
            "{U}{U}{U}, {T}: Return target permanent to its owner's hand");

        var bounce = activated.Single();

        // {U}{U}{U} — three coloured pips (TotalValue 3).
        bounce.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue
            .Should().Be(3, "the {U}{U}{U} pips are a 3-mana requirement");

        // {T} — self-tap.
        bounce.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap);

        bounce.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Contain("permanent");
    }

    [Fact]
    public void TemporalAdept_BounceAbility_ReturnsChosenPermanentToItsOwnersHand()
    {
        var adept = TemporalAdeptFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(adept);
        adept.SetZone(ZoneType.Battlefield);

        // Bob's permanent — must return to BOB's hand, not Alice's (the
        // activating controller).
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var bounce = adept.Abilities.OfType<ActivatedAbility>().Single();
        bounce.SetChosenTargets(new[] { new object[] { grizzly } });

        bounce.Resolve();

        grizzly.Zone.Should().Be(ZoneType.Hand,
            "Fx.BounceToHand moves the chosen permanent to its owner's hand (CR 701.20)");
        _bob.Zones.Hand.GetCards().Should().Contain(grizzly,
            "the bounce lands in the TARGET's OWNER's hand (Bob), not the controller's");
        _alice.Zones.Hand.GetCards().Should().NotContain(grizzly);
    }

    [Fact]
    public void TemporalAdept_BounceAbility_NoOpOnNonBattlefieldTarget()
    {
        var adept = TemporalAdeptFactory.Create(_alice);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        // Deliberately NOT on the battlefield — CR 608.2b recheck rejects it.

        var bounce = adept.Abilities.OfType<ActivatedAbility>().Single();
        bounce.SetChosenTargets(new[] { new object[] { grizzly } });

        bounce.Resolve();

        grizzly.Zone.Should().NotBe(ZoneType.Hand,
            "CR 608.2b — target no longer on battlefield: effect fails silently");
    }
}
