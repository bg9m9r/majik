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
/// Tests for <see cref="BlastZoneFactory"/> (War of the Spark / Commander
/// Masters). Land:
///   "This land enters with a charge counter on it.
///    {T}: Add {C}.
///    {X}{X}, {T}: Put X charge counters on this land.
///    {3}, {T}, Sacrifice this land: Destroy each nonland permanent with
///    mana value equal to the number of charge counters on this land."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB charge-counter trigger.
/// - {T}: Add {C} mana ability.
/// - {X}{X}, {T}: put X charge counters (X-provider sampled at resolution).
/// - {3}, {T}, Sacrifice sweep destroys nonland permanents with mv = charge
///   counters on all battlefields; lands + non-matching mv survive.
/// - Sweep sacrifices Blast Zone itself (CR 701.16).
/// - Sweep activated ability is sorcery-speed (CR 117.1a / 307.5).
/// </summary>
public class BlastZoneTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BlastZone_Identity()
    {
        var land = BlastZoneFactory.Create(_alice);

        land.Name.Should().Be("Blast Zone");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Blast Zone is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BlastZone_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Blast Zone", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Blast Zone");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB charge-counter trigger is attached for shape");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} is wired");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the charge-counter activation + sweep activation are both attached");
    }

    // -----------------------------------------------------------------------
    // ETB charge-counter trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void BlastZone_EtbTrigger_AddsOneChargeCounter()
    {
        var land = BlastZoneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "no counters until the ETB trigger fires");

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        land.Counters.Count(CounterType.Charge).Should().Be(1,
            "ETB trigger adds exactly one charge counter (CR 614.1d-style)");
    }

    // -----------------------------------------------------------------------
    // {X}{X}, {T}: put X charge counters
    // -----------------------------------------------------------------------

    [Fact]
    public void BlastZone_ChargeActivation_AddsXChargeCounters()
    {
        var land = BlastZoneFactory.Create(
            _alice,
            chargeXValueProvider: () => 3,
            allPlayersResolver: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // The first activated ability is the {X}{X}, {T} charge counter add.
        var chargeAbility = land.Abilities.OfType<ActivatedAbility>()
            .First(a => !a.IsSorcerySpeed);

        chargeAbility.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed cost has a single mana component {X}{X}");
        chargeAbility.Costs.OfType<AdditionalCost>().Should().ContainSingle(c =>
            c.CostType == AdditionalCostType.Tap,
            "the second cost is the tap rider");

        foreach (var e in chargeAbility.Effects) e.Execute();

        land.Counters.Count(CounterType.Charge).Should().Be(3,
            "X = 3 → put three charge counters on Blast Zone");
    }

    [Fact]
    public void BlastZone_ChargeActivation_WithX0_AddsNoCounters()
    {
        var land = BlastZoneFactory.Create(
            _alice,
            chargeXValueProvider: () => 0,
            allPlayersResolver: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var chargeAbility = land.Abilities.OfType<ActivatedAbility>()
            .First(a => !a.IsSorcerySpeed);

        foreach (var e in chargeAbility.Effects) e.Execute();

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "X = 0 → no counters added");
    }

    // -----------------------------------------------------------------------
    // {3}, {T}, Sacrifice sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void BlastZone_SweepActivation_IsSorcerySpeed()
    {
        var land = BlastZoneFactory.Create(_alice);

        var sweep = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);

        sweep.IsSorcerySpeed.Should().BeTrue(
            "sweep carries the printed 'Activate only as a sorcery' rider (CR 117.1a)");

        sweep.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "sweep printed mana cost is {3}");
        sweep.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1, "tap cost");
        sweep.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(1, "sacrifice cost");
    }

    [Fact]
    public void BlastZone_Sweep_DestroysNonlandPermanentsWithMatchingMv()
    {
        // Seed Blast Zone with 2 charge counters then sweep — mv-2 targets
        // across both battlefields die; lands + non-matching mv survive.
        var land = BlastZoneFactory.Create(
            _alice,
            chargeXValueProvider: null,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Charge, 2);

        // Alice: mv-2 bear (destroy), mv-1 artifact (survive), mv-3 giant
        // (survive), Mountain (survive — Land excluded).
        var aliceBear = new Creature("Grizzly Bears", "1G", 2, 2);
        aliceBear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(aliceBear);
        aliceBear.SetZone(ZoneType.Battlefield);

        var aliceArtifact = new Artifact("Mishra's Bauble", "0");
        aliceArtifact.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(aliceArtifact);
        aliceArtifact.SetZone(ZoneType.Battlefield);

        var aliceGiant = new Creature("Hill Giant", "3R", 3, 3);
        aliceGiant.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(aliceGiant);
        aliceGiant.SetZone(ZoneType.Battlefield);

        var mountain = NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        // Bob: mv-2 enchantment (destroy), mv-4 enchantment (survive).
        var bobAura = new Enchantment("Some Aura", "1B");
        bobAura.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bobAura);
        bobAura.SetZone(ZoneType.Battlefield);

        var bobBig = new Enchantment("Big Enchantment", "2BB");
        bobBig.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bobBig);
        bobBig.SetZone(ZoneType.Battlefield);

        var sweep = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);
        foreach (var e in sweep.Effects) e.Execute();

        aliceBear.Zone.Should().Be(ZoneType.Graveyard, "mv-2 creature destroyed");
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceBear);

        aliceArtifact.Zone.Should().Be(ZoneType.Battlefield,
            "mv-0 artifact survives (mv ≠ 2)");
        aliceGiant.Zone.Should().Be(ZoneType.Battlefield,
            "mv-3 creature survives (mv ≠ 2)");
        mountain.Zone.Should().Be(ZoneType.Battlefield,
            "Land excluded from the nonland predicate");

        bobAura.Zone.Should().Be(ZoneType.Graveyard,
            "Bob's mv-2 enchantment destroyed (sweep crosses battlefields)");
        bobBig.Zone.Should().Be(ZoneType.Battlefield,
            "mv-4 enchantment survives");
    }

    [Fact]
    public void BlastZone_Sweep_SacrificesBlastZoneItself()
    {
        var land = BlastZoneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var sweep = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);
        foreach (var e in sweep.Effects) e.Execute();

        land.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost moves Blast Zone to its owner's graveyard (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }
}
