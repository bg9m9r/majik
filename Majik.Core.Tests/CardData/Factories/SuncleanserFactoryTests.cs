using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SuncleanserFactory"/>.
///
/// Card: Suncleanser — Creature — Human Cleric 1/4, {1}{W} (Core Set 2021).
///   "When this creature enters, choose one —
///    • Remove all counters from target creature. It can't have counters put
///      on it for as long as this creature remains on the battlefield.
///    • Target opponent loses all counters. That player can't get counters
///      for as long as this creature remains on the battlefield."
///
/// Covers: identity / dispatch; player-mode wipe + lock; per-player scoping;
/// battlefield-gated revoke; energy / poison / experience all locked;
/// creature-mode wipe + lock.
/// </summary>
public class SuncleanserFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Suncleanser_Identity()
    {
        var c = SuncleanserFactory.Create(_alice);

        c.Name.Should().Be("Suncleanser");
        c.ManaCost.Should().Be("{1}{W}");
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(4);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Suncleanser()
    {
        var card = NamedCardFactory.Create("Suncleanser", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Suncleanser");
        card.ManaCost.Should().Be("{1}{W}");
    }

    // ── Mode 1 (player): wipe + lock ─────────────────────────────────────

    [Fact]
    public void PlayerMode_RemovesAllExistingCounters_FromChosenPlayer()
    {
        // Bob has 3 energy + 2 poison + 1 experience before Suncleanser hits.
        _bob.GainEnergy(3);
        _bob.AddPoisonCounters(2);
        _bob.AddCounters(CounterType.Experience, 1);

        var bus = new ReplacementBus();
        var sun = SuncleanserFactory.Create(_alice, SuncleanserFactory.ModePlayer, bus, triggers: null);
        PlaceOnBattlefield(sun, _alice);

        ResolveEtbTargetingPlayer(sun, _bob);

        _bob.EnergyCounters.Should().Be(0, "Suncleanser wiped all counters (CR 122)");
        _bob.PoisonCounters.Should().Be(0);
        _bob.GetCounters(CounterType.Experience).Should().Be(0);
    }

    [Fact]
    public void PlayerMode_LockedPlayer_CantGetEnergy()
    {
        var bus = new ReplacementBus();
        _bob.AttachReplacementBus(bus);

        var sun = SuncleanserFactory.Create(_alice, SuncleanserFactory.ModePlayer, bus, triggers: null);
        PlaceOnBattlefield(sun, _alice);
        ResolveEtbTargetingPlayer(sun, _bob);

        _bob.GainEnergy(4);

        _bob.EnergyCounters.Should().Be(0, "the locked player can't get counters (CR 614)");
    }

    [Fact]
    public void PlayerMode_LockedPlayer_CantGetPoison()
    {
        var bus = new ReplacementBus();
        _bob.AttachReplacementBus(bus);

        var sun = SuncleanserFactory.Create(_alice, SuncleanserFactory.ModePlayer, bus, triggers: null);
        PlaceOnBattlefield(sun, _alice);
        ResolveEtbTargetingPlayer(sun, _bob);

        _bob.AddPoisonCounters(3);

        _bob.PoisonCounters.Should().Be(0, "poison is a player counter and is locked too");
    }

    [Fact]
    public void PlayerMode_OnlyChosenPlayerIsLocked()
    {
        var bobBus = new ReplacementBus();
        var carolBus = new ReplacementBus();
        var carol = new Player("Carol", 20);
        _bob.AttachReplacementBus(bobBus);
        carol.AttachReplacementBus(carolBus);

        // Suncleanser is registered on Bob's bus (he's the chosen target);
        // Carol routes through her own (un-locked) bus.
        var sun = SuncleanserFactory.Create(_alice, SuncleanserFactory.ModePlayer, bobBus, triggers: null);
        PlaceOnBattlefield(sun, _alice);
        ResolveEtbTargetingPlayer(sun, _bob);

        _bob.GainEnergy(2);
        carol.GainEnergy(2);

        _bob.EnergyCounters.Should().Be(0, "Bob is locked");
        carol.EnergyCounters.Should().Be(2, "Carol is not the chosen target — she keeps getting counters");
    }

    [Fact]
    public void PlayerMode_LockRevokes_WhenSuncleanserLeavesBattlefield()
    {
        var bus = new ReplacementBus();
        _bob.AttachReplacementBus(bus);

        var sun = SuncleanserFactory.Create(_alice, SuncleanserFactory.ModePlayer, bus, triggers: null);
        PlaceOnBattlefield(sun, _alice);
        ResolveEtbTargetingPlayer(sun, _bob);

        _bob.GainEnergy(2);
        _bob.EnergyCounters.Should().Be(0, "locked while Suncleanser is on the battlefield");

        // Suncleanser leaves the battlefield → the lock auto-revokes (CR 614.6).
        _alice.Zones.Battlefield.RemoveCard(sun);
        _alice.Zones.Graveyard.AddCard(sun);
        sun.SetZone(ZoneType.Graveyard);

        _bob.GainEnergy(5);
        _bob.EnergyCounters.Should().Be(5, "with Suncleanser gone, the player can get counters again");
    }

    // ── Mode 0 (creature): wipe + lock ───────────────────────────────────

    [Fact]
    public void CreatureMode_WipesAndLocksTargetCreature()
    {
        var bus = new ReplacementBus();
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        PlaceCreatureOnBattlefield(bear, _alice);
        bear.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var sun = SuncleanserFactory.Create(_alice, SuncleanserFactory.ModeCreature, bus, triggers: null);
        PlaceOnBattlefield(sun, _alice);
        ResolveEtbTargetingCreature(sun, bear);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0, "all counters wiped");

        // Locked: future counter placement (via CountersService) is prevented.
        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 2, bus);
        placed.Should().Be(0, "the creature can't have counters put on it while Suncleanser is in play");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void PlaceOnBattlefield(Creature c, Player owner)
    {
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }

    private static void PlaceCreatureOnBattlefield(Creature c, Player owner)
    {
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }

    private static void ResolveEtbTargetingPlayer(Creature suncleanser, Player target)
    {
        var etb = suncleanser.Abilities.OfType<TriggeredAbility>().Single();
        // Slot 0 (creature) empty, slot 1 (player) = target.
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { target },
        });
        foreach (var e in etb.Effects) e.Execute();
    }

    private static void ResolveEtbTargetingCreature(Creature suncleanser, Permanent target)
    {
        var etb = suncleanser.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
            Array.Empty<object>(),
        });
        foreach (var e in etb.Effects) e.Execute();
    }
}
