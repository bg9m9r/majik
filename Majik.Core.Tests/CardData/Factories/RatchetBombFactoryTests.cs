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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RatchetBombFactory"/>. Artifact ({2}):
///   "{T}: Put a charge counter on this artifact.
///    {T}, Sacrifice this artifact: Destroy each nonland permanent with
///    mana value equal to the number of charge counters on this artifact."
///
/// Analogue: <see cref="BlastZoneFactory"/> — same charge-counter accrual +
/// "{T}, Sacrifice: destroy each nonland permanent with mv = charge counters"
/// sweep. Ratchet Bomb drops Blast Zone's land mana ability, its ETB
/// charge-counter trigger, and the {X}{X} charge-counter activation; it
/// accrues one counter per {T} activation instead.
///
/// Covers:
/// - Identity (Artifact, name, mana cost {2}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch + ability shape (two
///   ActivatedAbilities, no ManaAbility, no TriggeredAbility).
/// - {T}: put a charge counter — adds exactly one charge counter.
/// - {T}, Sacrifice sweep destroys nonland permanents with mv = charge
///   counters across all battlefields; lands + non-matching mv survive.
/// - Sweep sacrifices Ratchet Bomb itself (CR 701.16).
/// - Sweep is instant speed (no "activate only as a sorcery" rider on this
///   card — unlike Blast Zone).
/// </summary>
[Trait("Color", "C")]
public class RatchetBombFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RatchetBomb_Identity()
    {
        var bomb = RatchetBombFactory.Create(_alice);

        bomb.Name.Should().Be("Ratchet Bomb");
        bomb.HasType(CardType.Artifact).Should().BeTrue();
        bomb.ManaCostValue.TotalValue.Should().Be(2, "Ratchet Bomb costs {2}");
        bomb.Owner.Should().BeSameAs(_alice);
        bomb.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // {T}: Put a charge counter on this artifact.
    // -----------------------------------------------------------------------

    [Fact]
    public void RatchetBomb_ChargeActivation_AddsOneChargeCounter()
    {
        var bomb = RatchetBombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var chargeAbility = ChargeAbility(bomb);

        chargeAbility.Costs.OfType<AdditionalCost>().Should().ContainSingle(c =>
            c.CostType == AdditionalCostType.Tap,
            "the only cost is the tap rider");
        chargeAbility.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "{T}: put a charge counter carries no mana cost");

        foreach (var e in chargeAbility.Effects) e.Execute();

        bomb.Counters.Count(CounterType.Charge).Should().Be(1,
            "each activation adds exactly one charge counter");

        foreach (var e in chargeAbility.Effects) e.Execute();
        bomb.Counters.Count(CounterType.Charge).Should().Be(2,
            "a second activation adds a second charge counter");
    }

    // -----------------------------------------------------------------------
    // {T}, Sacrifice sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void RatchetBomb_SweepActivation_CostsAreTapAndSacrifice()
    {
        var bomb = RatchetBombFactory.Create(_alice);

        var sweep = SweepAbility(bomb);

        sweep.IsSorcerySpeed.Should().BeFalse(
            "Ratchet Bomb's sweep has no 'activate only as a sorcery' rider");
        sweep.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the sweep carries no mana cost (unlike Blast Zone's {3})");
        sweep.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1, "tap cost");
        sweep.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(1, "sacrifice cost");
    }

    [Fact]
    public void RatchetBomb_Sweep_DestroysNonlandPermanentsWithMatchingMv()
    {
        // Seed Ratchet Bomb with 2 charge counters then sweep — mv-2 targets
        // across both battlefields die; lands + non-matching mv survive.
        var bomb = RatchetBombFactory.Create(
            _alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);
        bomb.Counters.Add(CounterType.Charge, 2);

        // Alice: mv-2 bear (destroy), mv-0 artifact (survive), mv-3 giant
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

        var sweep = SweepAbility(bomb);
        foreach (var e in sweep.Effects) e.Execute();

        aliceBear.Zone.Should().Be(ZoneType.Graveyard, "mv-2 creature destroyed");
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceBear);

        aliceArtifact.Zone.Should().Be(ZoneType.Battlefield,
            "mv-0 artifact survives (mv != 2)");
        aliceGiant.Zone.Should().Be(ZoneType.Battlefield,
            "mv-3 creature survives (mv != 2)");
        mountain.Zone.Should().Be(ZoneType.Battlefield,
            "Land excluded from the nonland predicate");

        bobAura.Zone.Should().Be(ZoneType.Graveyard,
            "Bob's mv-2 enchantment destroyed (sweep crosses battlefields)");
        bobBig.Zone.Should().Be(ZoneType.Battlefield,
            "mv-4 enchantment survives");
    }

    [Fact]
    public void RatchetBomb_Sweep_WithZeroCounters_DestroysMvZeroPermanents()
    {
        // CR: with no charge counters, mana value 0 nonland permanents are
        // destroyed (a freshly-cast Ratchet Bomb wipes all mv-0 nonlands).
        var bomb = RatchetBombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var token = new Artifact("Treasure", "0");
        token.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var sweep = SweepAbility(bomb);
        foreach (var e in sweep.Effects) e.Execute();

        token.Zone.Should().Be(ZoneType.Graveyard,
            "mv-0 nonland destroyed when charge count is 0");
        bear.Zone.Should().Be(ZoneType.Battlefield, "mv-2 survives");
    }

    [Fact]
    public void RatchetBomb_Sweep_SacrificesRatchetBombItself()
    {
        var bomb = RatchetBombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var sweep = SweepAbility(bomb);
        foreach (var e in sweep.Effects) e.Execute();

        bomb.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost moves Ratchet Bomb to its owner's graveyard (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
    }

    // -----------------------------------------------------------------------
    // Helpers — the charge ability is the non-sacrifice activated ability;
    // the sweep is the one carrying the sacrifice cost.
    // -----------------------------------------------------------------------

    private static ActivatedAbility ChargeAbility(Artifact bomb) =>
        bomb.Abilities.OfType<ActivatedAbility>().Single(a =>
            !a.Costs.OfType<AdditionalCost>().Any(c => c.CostType == AdditionalCostType.Sacrifice));

    private static ActivatedAbility SweepAbility(Artifact bomb) =>
        bomb.Abilities.OfType<ActivatedAbility>().Single(a =>
            a.Costs.OfType<AdditionalCost>().Any(c => c.CostType == AdditionalCostType.Sacrifice));
}
