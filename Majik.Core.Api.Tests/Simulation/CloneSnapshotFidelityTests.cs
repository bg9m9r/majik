using System.Text.Json;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Api.Tests.Simulation;

/// <summary>
/// Catch-all fidelity oracle: builds a representative mid-game board, clones
/// it, snapshots both originals and clones, and asserts the serialised DTOs
/// are byte-identical. Any runtime field that GameStateCloner misses will
/// surface here as a diff in the JSON.
/// </summary>
public sealed class CloneSnapshotFidelityTests
{
    [Fact]
    public void Clone_ProducesIdenticalSnapshot_ForMixedBoard()
    {
        var (alice, bob, stack) = BuildMixedMidGameBoard();

        var liveDto = StateSnapshotter.Snapshot(
            gameId: Guid.Empty,
            turnNumber: 3,
            phase: StepStateType.PreCombatMain,
            activePlayer: alice,
            players: new[] { alice, bob },
            stack: stack);

        var cloned = GameStateCloner.Clone(new[] { alice, bob }, stack);

        // The cloned stack must be non-null because we passed one in.
        cloned.Stack.Should().NotBeNull();

        var cloneDto = StateSnapshotter.Snapshot(
            gameId: Guid.Empty,
            turnNumber: 3,
            phase: StepStateType.PreCombatMain,
            activePlayer: cloned.PlayerFor(alice),
            players: cloned.Players,
            stack: cloned.Stack!);

        Serialize(cloneDto).Should().Be(Serialize(liveDto));
    }

    /// <summary>
    /// Builds a representative mixed mid-game board:
    /// - Alice (20 life, 2 energy, 1 poison, floating {R}{R}) with:
    ///   - Battlefield: tapped creature, untapped creature with damage,
    ///     creature with +1/+1 counter + no summoning sickness,
    ///     two lands (one tapped, one untapped)
    ///   - Hand: one instant
    ///   - Graveyard: one sorcery
    ///   - Exile: one instant
    /// - Bob (14 life) with:
    ///   - Battlefield: one creature, one land
    /// - Stack: Lightning Bolt (Alice's) targeting Bob.
    /// </summary>
    private static (Player alice, Player bob, Majik.Core.Stack.Stack stack)
        BuildMixedMidGameBoard()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        // Bob takes 6 damage → 14 life.
        bob.LoseLife(6);

        // Alice energy + poison counters.
        alice.GainEnergy(2);
        alice.AddPoisonCounters(1);

        // Alice floating mana {R}{R}.
        alice.AddManaToPool(ManaCost.Parse("{R}{R}"));

        // ── Alice battlefield ──────────────────────────────────────────────

        // 1. Tapped creature (fresh; has summoning sickness by default).
        var tapBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        tapBear.ChangeOwner(alice);
        tapBear.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(tapBear);
        tapBear.Tap();

        // 2. Untapped creature with 1 marked combat damage.
        var damagedBear = new Creature("Hill Giant", "{3}{R}", 3, 3);
        damagedBear.ChangeOwner(alice);
        damagedBear.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(damagedBear);
        damagedBear.TakeDamage(1);

        // 3. Creature with a +1/+1 counter; no summoning sickness.
        var pumped = new Creature("Llanowar Elves", "{G}", 1, 1);
        pumped.ChangeOwner(alice);
        pumped.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(pumped);
        pumped.ClearSummoningSickness();
        pumped.Counters.Add(CounterType.PlusOnePlusOne, 1);

        // 4. A tapped land and an untapped land.
        var tappedForest = new Land("Forest");
        tappedForest.ChangeOwner(alice);
        tappedForest.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(tappedForest);
        tappedForest.Tap();

        var untappedForest = new Land("Forest");
        untappedForest.ChangeOwner(alice);
        untappedForest.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(untappedForest);

        // ── Alice non-battlefield zones ────────────────────────────────────

        var handCard = new Instant("Counterspell", "{U}{U}");
        handCard.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(handCard);

        var gyCard = new Sorcery("Divination", "{2}{U}");
        gyCard.ChangeOwner(alice);
        alice.Zones.Graveyard.AddCard(gyCard);

        var exileCard = new Instant("Path to Exile", "{W}");
        exileCard.ChangeOwner(alice);
        alice.Zones.Exile.AddCard(exileCard);

        // ── Bob battlefield ────────────────────────────────────────────────

        var bobCreature = new Creature("Goblin Guide", "{R}", 2, 2);
        bobCreature.ChangeOwner(bob);
        bobCreature.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(bobCreature);

        var bobLand = new Land("Mountain");
        bobLand.ChangeOwner(bob);
        bobLand.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(bobLand);

        // ── Stack: Lightning Bolt targeting Bob ────────────────────────────

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(bolt);

        var bus   = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var spell = new Majik.Core.Spells.Spell(
            card: bolt,
            controller: alice,
            targets: new[] { Target.Player(bob) });
        stack.Push(spell);

        return (alice, bob, stack);
    }

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = false,
        // System.Text.Json serialises properties deterministically in declaration
        // order (C# record order) for records. Dictionary keys follow insertion
        // order. Both are stable here — no additional sorting needed.
    };

    private static string Serialize<T>(T dto) => JsonSerializer.Serialize(dto, _opts);
}
