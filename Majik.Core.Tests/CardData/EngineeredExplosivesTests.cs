using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EngineeredExplosivesFactory"/> (Fifth Dawn /
/// Modern Horizons, {X}).
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Sunburst ETB: applies X charge counters via the v1 X-value provider.
/// - {2}, Sacrifice activated ability shape: mana cost + sacrifice cost.
/// - Sweep destroys nonland permanents with mv = charge counters on both
///   sides of the battlefield.
/// - Sweep sacrifices Engineered Explosives itself.
/// - Lands are immune.
/// - Permanents with non-matching mana value are immune.
/// </summary>
public class EngineeredExplosivesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EngineeredExplosives_Identity()
    {
        var ee = EngineeredExplosivesFactory.Create(_alice);

        ee.Name.Should().Be("Engineered Explosives");
        ee.ManaCost.Should().Be("{X}");
        ee.HasType(CardType.Artifact).Should().BeTrue();
        ee.Owner.Should().BeSameAs(_alice);
        ee.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EngineeredExplosives_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Engineered Explosives", _alice);

        card.Should().BeOfType<Artifact>("Engineered Explosives is an Artifact");
        card.Name.Should().Be("Engineered Explosives");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Sunburst ETB trigger is surfaced for shape");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{2}, Sacrifice: sweep is surfaced for shape");
    }

    // -----------------------------------------------------------------------
    // Sunburst ETB
    // -----------------------------------------------------------------------

    [Fact]
    public void EngineeredExplosives_EtbTrigger_WithXProvider_AddsXChargeCounters()
    {
        var ee = EngineeredExplosivesFactory.Create(
            _alice, xValueProvider: () => 2, allPlayersResolver: null);
        _alice.Zones.Battlefield.AddCard(ee);
        ee.SetZone(ZoneType.Battlefield);

        ee.Counters.Count(CounterType.Charge).Should().Be(0);

        var trigger = ee.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        ee.Counters.Count(CounterType.Charge).Should().Be(2,
            "Sunburst applies X charge counters at v1 (X provider = 2)");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void EngineeredExplosives_ActivatedAbility_Has2ManaPlusSacrificeCost()
    {
        var ee = EngineeredExplosivesFactory.Create(_alice);

        var ability = ee.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            c => c.Cost.TotalValue == 2,
            "the printed mana cost on the activation is {2}");

        var sac = ability.Costs.OfType<AdditionalCost>().Single();
        sac.CostType.Should().Be(AdditionalCostType.Sacrifice,
            "the second cost is sacrificing Engineered Explosives itself");
    }

    // -----------------------------------------------------------------------
    // Sweep — both battlefields, mv-matched, lands untouched
    // -----------------------------------------------------------------------

    [Fact]
    public void EngineeredExplosives_Activate_DestroysNonlandPermanentsOnBothSidesWithMatchingMv()
    {
        var ee = EngineeredExplosivesFactory.Create(
            _alice,
            xValueProvider: null,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(ee);
        ee.SetZone(ZoneType.Battlefield);
        ee.Counters.Add(CounterType.Charge, 2);

        // Alice: mv-2 bear (target).
        var aliceBear = new Creature("Grizzly Bears", "1G", 2, 2);
        aliceBear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(aliceBear);
        aliceBear.SetZone(ZoneType.Battlefield);

        // Bob: mv-2 artifact (target).
        var bobArtifact = new Artifact("Mind Stone", "2");
        bobArtifact.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bobArtifact);
        bobArtifact.SetZone(ZoneType.Battlefield);

        var ability = ee.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        aliceBear.Zone.Should().Be(ZoneType.Graveyard,
            "Alice's mv-2 creature is destroyed");
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceBear);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(aliceBear);

        bobArtifact.Zone.Should().Be(ZoneType.Graveyard,
            "Bob's mv-2 artifact is destroyed (sweep crosses both battlefields)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobArtifact);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobArtifact);
    }

    [Fact]
    public void EngineeredExplosives_Activate_SacrificesEngineeredExplosivesItself()
    {
        var ee = EngineeredExplosivesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ee);
        ee.SetZone(ZoneType.Battlefield);
        ee.Counters.Add(CounterType.Charge, 1);

        var ability = ee.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        ee.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost moves Engineered Explosives to its owner's graveyard (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(ee);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(ee);
    }

    [Fact]
    public void EngineeredExplosives_Activate_LandsAreImmune()
    {
        var ee = EngineeredExplosivesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ee);
        ee.SetZone(ZoneType.Battlefield);
        // 0 counters → target mv = 0. Basic lands have mv 0 and would
        // match the mv predicate if the land filter weren't applied.

        var mountain = NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        var ability = ee.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        mountain.Zone.Should().Be(ZoneType.Battlefield,
            "Lands are excluded from the 'nonland permanent' predicate even at mv 0");
        _alice.Zones.Battlefield.GetCards().Should().Contain(mountain);
    }

    [Fact]
    public void EngineeredExplosives_Activate_NonMatchingManaValueIsImmune()
    {
        var ee = EngineeredExplosivesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ee);
        ee.SetZone(ZoneType.Battlefield);
        ee.Counters.Add(CounterType.Charge, 2);

        // mv-3 creature — should survive (mv ≠ 2).
        var giant = new Creature("Hill Giant", "3R", 3, 3);
        giant.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(giant);
        giant.SetZone(ZoneType.Battlefield);

        // mv-2 creature — should be destroyed.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var ability = ee.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        giant.Zone.Should().Be(ZoneType.Battlefield,
            "mv-3 permanent survives — only mv-2 nonland permanents are swept");
        _alice.Zones.Battlefield.GetCards().Should().Contain(giant);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "mv-2 nonland permanent is destroyed");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }
}
