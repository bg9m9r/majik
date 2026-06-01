using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 702.54 — Bloodthirst N. Reusable <see cref="BloodthirstReplacement"/>
/// ETB-counter replacement, proven via Bloodrage Vampire (N=1) and Gorehorn
/// Minotaurs (N=2). Counters land iff an opponent was dealt damage this turn.
/// </summary>
public class BloodthirstReplacementTests
{
    private static void EnterBattlefield(Creature card, Player owner, ReplacementBus bus)
    {
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    [Fact]
    public void WasDealtDamageThisTurn_LatchesOnDamage_AndResetsOnTurnTrackerReset()
    {
        var bob = new Player("Bob", 20);
        bob.WasDealtDamageThisTurn.Should().BeFalse();

        bob.RecordDamageDealt(3);
        bob.WasDealtDamageThisTurn.Should().BeTrue();

        // Zero "damage" is not damage (CR 120.3).
        var carol = new Player("Carol", 20);
        carol.RecordDamageDealt(0);
        carol.WasDealtDamageThisTurn.Should().BeFalse();

        bob.ResetTurnTrackers();
        bob.WasDealtDamageThisTurn.Should().BeFalse("the flag clears at turn start");
    }

    [Fact]
    public void Bloodthirst1_NoOpponentDamaged_EntersVanilla()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new ReplacementBus();

        var card = BloodrageVampireFactory.Create(alice, bus, () => new[] { bob });
        EnterBattlefield(card, alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no opponent took damage this turn → enters as a vanilla 3/1");
    }

    [Fact]
    public void Bloodthirst1_OpponentDamaged_EntersWithOneCounter()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new ReplacementBus();

        var card = BloodrageVampireFactory.Create(alice, bus, () => new[] { bob });

        // An opponent was dealt damage earlier this turn.
        bob.RecordDamageDealt(2);

        EnterBattlefield(card, alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Bloodthirst 1 → enters with one +1/+1 counter when an opponent was damaged");
    }

    [Fact]
    public void Bloodthirst2_OpponentDamaged_EntersWithTwoCounters()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new ReplacementBus();

        var card = GorehornMinotaursFactory.Create(alice, bus, () => new[] { bob });
        bob.RecordDamageDealt(1);

        EnterBattlefield(card, alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "Bloodthirst 2 → enters with two +1/+1 counters when an opponent was damaged");
    }

    [Fact]
    public void Bloodthirst_OnlyCountsOpponentDamage_NotControllerDamage()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new ReplacementBus();

        var card = BloodrageVampireFactory.Create(alice, bus, () => new[] { bob });

        // The controller took damage — that must NOT satisfy Bloodthirst.
        alice.RecordDamageDealt(5);

        EnterBattlefield(card, alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Bloodthirst keys on opponent damage, not the controller's own damage");
    }
}
