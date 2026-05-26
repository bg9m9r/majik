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
/// Tests for <see cref="OblivionStoneFactory"/> — Artifact {3} (Mirrodin):
///   "{4}, {T}: Put a fate counter on each nonland permanent.
///    {5}, {T}, Sacrifice Oblivion Stone: Destroy each nonland permanent
///    without a fate counter on it. Then remove all fate counters from
///    all permanents."
///
/// Covers:
/// - Identity (Artifact, {3}, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Ability shape: two activated abilities at the correct costs.
/// - Mark mode: fate counter on every nonland permanent across all
///   battlefields; lands skipped; idempotent on re-activation.
/// - Sweep mode: destroys nonland permanents without fate counters;
///   marked permanents survive; fate counters cleared after; lands
///   spared.
/// - Sweep sacrifices Oblivion Stone (CR 701.16).
/// </summary>
public class OblivionStoneTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void OblivionStone_IsArtifact_AtCost3()
    {
        var stone = OblivionStoneFactory.Create(_alice);

        stone.Name.Should().Be("Oblivion Stone");
        stone.ManaCost.Should().Be("{3}");
        stone.HasType(CardType.Artifact).Should().BeTrue();
        stone.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Oblivion Stone is a non-legendary artifact");
        stone.Owner.Should().BeSameAs(_alice);
        stone.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OblivionStone()
    {
        var card = NamedCardFactory.Create("Oblivion Stone", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Oblivion Stone");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void OblivionStone_AbilityShape_TwoActivated()
    {
        var stone = OblivionStoneFactory.Create(_alice);
        var abilities = stone.Abilities.OfType<ActivatedAbility>().ToList();

        abilities.Should().HaveCount(2);

        // Mark ability — {4}, {T} → mana cost {4} + tap + no sacrifice.
        var mark = abilities.Single(a =>
            a.Costs.OfType<AdditionalCost>().All(c => c.CostType != AdditionalCostType.Sacrifice));
        mark.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            c => c.Description.Contains("4"), "mark mode is {4}");
        mark.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1);

        // Sweep ability — {5}, {T}, Sacrifice.
        var sweep = abilities.Single(a =>
            a.Costs.OfType<AdditionalCost>().Any(c => c.CostType == AdditionalCostType.Sacrifice));
        sweep.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            c => c.Description.Contains("5"), "sweep mode is {5}");
        sweep.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1);
        sweep.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // {4}, {T}: Put a fate counter on each nonland permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void Mark_AddsFateCounterToEachNonlandPermanent_AcrossBoth()
    {
        var stone = OblivionStoneFactory.Create(
            _alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(stone);
        stone.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var aura = new Enchantment("Pacifism", "{1}{W}");
        aura.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var forest = (Permanent)NamedCardFactory.Create("Forest", _alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var mark = stone.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .All(c => c.CostType != AdditionalCostType.Sacrifice));
        foreach (var e in mark.Effects) e.Execute();

        bear.Counters.Count(CounterType.Fate).Should().Be(1);
        aura.Counters.Count(CounterType.Fate).Should().Be(1);
        stone.Counters.Count(CounterType.Fate).Should().Be(1,
            "Oblivion Stone itself is a nonland permanent — gets a fate counter");
        forest.Counters.Count(CounterType.Fate).Should().Be(0,
            "Lands are excluded");
    }

    [Fact]
    public void Mark_Idempotent_OnReactivation()
    {
        var stone = OblivionStoneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(stone);
        stone.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var mark = stone.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .All(c => c.CostType != AdditionalCostType.Sacrifice));

        foreach (var e in mark.Effects) e.Execute();
        foreach (var e in mark.Effects) e.Execute();
        foreach (var e in mark.Effects) e.Execute();

        bear.Counters.Count(CounterType.Fate).Should().Be(1,
            "re-activation doesn't stack fate counters past one");
    }

    // -----------------------------------------------------------------------
    // {5}, {T}, Sacrifice: Destroy each nonland permanent without a fate
    // counter on it. Then remove all fate counters from all permanents.
    // -----------------------------------------------------------------------

    [Fact]
    public void Sweep_DestroysNonlandPermanentsWithoutFateCounter_AcrossBoth()
    {
        var stone = OblivionStoneFactory.Create(
            _alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(stone);
        stone.SetZone(ZoneType.Battlefield);

        // Alice: marked bear survives; unmarked giant dies; mountain
        // survives (Land excluded).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        bear.Counters.Add(CounterType.Fate, 1);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(giant);
        giant.SetZone(ZoneType.Battlefield);

        var mountain = (Permanent)NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        // Bob: unmarked aura dies; marked artifact survives.
        var aura = new Enchantment("Pacifism", "{1}{W}");
        aura.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var bobArtifact = new Artifact("Mishra's Bauble", "{0}");
        bobArtifact.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bobArtifact);
        bobArtifact.SetZone(ZoneType.Battlefield);
        bobArtifact.Counters.Add(CounterType.Fate, 1);

        var sweep = stone.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Sacrifice));
        foreach (var e in sweep.Effects) e.Execute();

        // Unmarked nonland permanents destroyed.
        giant.Zone.Should().Be(ZoneType.Graveyard);
        aura.Zone.Should().Be(ZoneType.Graveyard);

        // Marked nonland permanents survive — fate counters cleared.
        bear.Zone.Should().Be(ZoneType.Battlefield);
        bear.Counters.Count(CounterType.Fate).Should().Be(0,
            "after-sweep clears all fate counters");
        bobArtifact.Zone.Should().Be(ZoneType.Battlefield);
        bobArtifact.Counters.Count(CounterType.Fate).Should().Be(0);

        // Lands always survive.
        mountain.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Sweep_SacrificesOblivionStoneItself()
    {
        var stone = OblivionStoneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(stone);
        stone.SetZone(ZoneType.Battlefield);

        var sweep = stone.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Sacrifice));
        foreach (var e in sweep.Effects) e.Execute();

        stone.Zone.Should().Be(ZoneType.Graveyard,
            "sacrifice cost moves Oblivion Stone to its owner's graveyard (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(stone);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(stone);
    }
}
