using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Production TDD test for Phase 2A Task B1 — continuous-effects re-registration.
///
/// Verifies that <see cref="GameStateCloner.Clone"/> re-applies continuous
/// effects (anthems / lords) on the cloned battlefield so that a creature under
/// a <see cref="LordStaticEffect"/> lord reads the BUFFED P/T in the search
/// sandbox WITHOUT any in-test re-registration hack.
///
/// Board: Alice controls Goblin Chieftain (2/2 lord; "Other Goblin creatures
/// you control have haste and get +1/+1") + a plain Goblin Warrior (2/2).
/// Live P/T of the Warrior = 3/3.  After Clone() the warrior in the cloned
/// game must ALSO read 3/3 (lord re-applies), and the Chieftain itself must
/// still read 2/2 (includeSelf: false).
/// </summary>
public sealed class ContinuousEffectsCloneTests
{
    [Fact]
    public void Clone_ReappliesLordAnthem_OnClonedCreatures()
    {
        // ── Arrange: live board with Goblin Chieftain lord ──────────────────
        var (alice, bob, chieftain, goblinWarrior) = BuildLiveLordBoard();

        // Live sanity: the anthem must apply before cloning.
        goblinWarrior.Power.Should().Be(3,
            "live: Goblin Warrior base 2/2 + Chieftain's +1/+1 lord = 3");
        goblinWarrior.Toughness.Should().Be(3,
            "live: Goblin Warrior base 2/2 + Chieftain's +1/+1 lord = 3");

        chieftain.Power.Should().Be(2,
            "live: Chieftain is 2/2 — its own lord doesn't buff itself (includeSelf: false)");
        chieftain.Toughness.Should().Be(2);

        // ── Act: clone ──────────────────────────────────────────────────────
        var cloned = GameStateCloner.Clone(new[] { alice, bob });

        // ── Assert: re-registration happened in the cloner ──────────────────
        var clonedWarrior  = (Creature)cloned.CardMap[goblinWarrior.InstanceId];
        var clonedChieftain = (Creature)cloned.CardMap[chieftain.InstanceId];

        clonedWarrior.ActiveEffects.Should().NotBeNull(
            "GameStateCloner must assign a fresh CES to cloned battlefield permanents");

        clonedWarrior.Power.Should().Be(3,
            "sandbox: Goblin Warrior gets +1/+1 from the re-registered Chieftain lord");
        clonedWarrior.Toughness.Should().Be(3,
            "sandbox: Goblin Warrior gets +1/+1 from the re-registered Chieftain lord");

        clonedChieftain.Power.Should().Be(2,
            "Chieftain is 2/2 — its own lord doesn't buff itself (includeSelf: false)");
        clonedChieftain.Toughness.Should().Be(2,
            "Chieftain is 2/2 — its own lord doesn't buff itself (includeSelf: false)");
    }

    [Fact]
    public void Clone_WithNoEffectsRegistered_LeavesActiveEffectsNull()
    {
        // A plain board with no ContinuousEffectsService should NOT have any
        // CES wired on the clone (no CES to discover, so no fresh CES built).
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.ChangeOwner(alice);
        bear.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        // Deliberately NOT wiring ActiveEffects on bear — simulates a board
        // with no live CES at all.

        var cloned = GameStateCloner.Clone(new[] { alice });
        var clonedBear = (Creature)cloned.CardMap[bear.InstanceId];

        clonedBear.ActiveEffects.Should().BeNull(
            "when no live CES is present, no fresh CES is created and ActiveEffects stays null");
        clonedBear.Power.Should().Be(2, "bare base power with no effects");
        clonedBear.Toughness.Should().Be(2);
    }

    [Fact]
    public void Clone_IndependenceFromLive_LordAnthem()
    {
        // Mutation of the clone must not affect the original, and vice versa.
        var (alice, bob, chieftain, goblinWarrior) = BuildLiveLordBoard();

        var cloned = GameStateCloner.Clone(new[] { alice, bob });

        var clonedWarrior = (Creature)cloned.CardMap[goblinWarrior.InstanceId];

        // Move the cloned warrior off the battlefield (simulates a sim action).
        cloned.Players[0].Zones.Graveyard.AddCard(clonedWarrior);

        // Original warrior remains buffed — clone mutation did not bleed through.
        goblinWarrior.Power.Should().Be(3,
            "live board unaffected by sandbox mutation");
    }

    // ── Board builder ────────────────────────────────────────────────────────

    private static (Player alice, Player bob, Creature chieftain, Creature goblinWarrior)
        BuildLiveLordBoard()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        // Shared ContinuousEffectsService — the "live" game's layer system.
        var ces = new ContinuousEffectsService();

        // Goblin Chieftain: 2/2, registers a LordStaticEffect:
        //   "Other Goblin creatures you control have haste and get +1/+1."
        var chieftain = GoblinChieftainFactory.Create(alice, ces);
        chieftain.ChangeOwner(alice);
        chieftain.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(chieftain); // sets Zone = Battlefield
        chieftain.ActiveEffects = ces;              // wire so lord IsActive() works

        // A plain Goblin Warrior (2/2) buffed by the Chieftain lord.
        var goblinWarrior = new Creature(
            "Goblin Warrior", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });
        goblinWarrior.ChangeOwner(alice);
        goblinWarrior.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(goblinWarrior); // sets Zone = Battlefield
        goblinWarrior.ActiveEffects = ces;              // wire so Compute sees the CES

        return (alice, bob, chieftain, goblinWarrior);
    }
}
