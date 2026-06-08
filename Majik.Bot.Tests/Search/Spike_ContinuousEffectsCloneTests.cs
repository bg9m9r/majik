using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.Factories;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// DIAGNOSTIC SPIKE — Phase 2A Task 2 (historical; bug now fixed in production).
///
/// Goal (original): determine whether cloned permanents' continuous effects
/// (lords / anthems) can be made to apply inside the search sandbox.
///
/// Resolution: Phase 2A Task B1 wired Approach C directly into
/// <see cref="GameStateCloner.Clone"/>. The tests below have been updated to
/// reflect the FIXED behaviour — a lord's anthem now re-applies automatically
/// on the cloned battlefield without any in-test hack.
///
/// ## Anthem chosen: Goblin Chieftain
/// GoblinChieftainFactory.Create(owner, ces) registers a LordStaticEffect:
///   - matchingSubtype: Goblin, power: +1, toughness: +1, includeSelf: false
///
/// ## Production hook: ContinuousEffect.CloneForSim + GameStateCloner Pass 5
/// LordStaticEffect.CloneForSim reconstructs the same effect pointing at the
/// cloned source.  GameStateCloner walks the live CES, calls CloneForSim for
/// each active effect whose source was cloned, registers results on a fresh CES,
/// and assigns it to every cloned battlefield permanent.
/// </summary>
public sealed class Spike_ContinuousEffectsCloneTests
{
    // ── Step 3 (historical): Confirm the bug → NOW FIXED ────────────────────

    /// <summary>
    /// Originally a RED test confirming the bug (anthem dropped on clone).
    /// Now documents the FIXED behaviour: GameStateCloner re-registers the lord
    /// so the cloned Goblin Warrior reads buffed P/T.
    /// </summary>
    [Fact]
    public void Spike_ClonedAnthem_DropsBuffInSandbox()
    {
        var (alice, _, chieftain, goblinWarrior, liveCes) = BuildLiveAnthemBoard();

        // LIVE check — the anthem must apply before cloning.
        goblinWarrior.Power.Should().Be(3, "live: Goblin Warrior 2/2 + Chieftain's +1/+1 = 3/3");
        goblinWarrior.Toughness.Should().Be(3);

        // Clone — Pass 5 now re-registers the lord effect.
        var cloned = GameStateCloner.Clone(new[] { alice, new Player("Bob", 20) });
        var clonedGoblinWarrior = (Creature)cloned.CardMap[goblinWarrior.InstanceId];

        // FIXED: anthem re-applies → clone evaluates as buffed 3/3.
        clonedGoblinWarrior.ActiveEffects.Should().NotBeNull(
            "GameStateCloner (Pass 5) assigns a fresh CES to cloned battlefield permanents");
        clonedGoblinWarrior.Power.Should().Be(3,
            "Goblin Warrior gets +1/+1 from the re-registered Chieftain lord");
        clonedGoblinWarrior.Toughness.Should().Be(3,
            "Goblin Warrior gets +1/+1 from the re-registered Chieftain lord");
    }

    // ── Step 5 (historical): Prototype fix → now production ─────────────────

    /// <summary>
    /// Prototype Approach C is now the production path.  The in-test
    /// RebuildLordEffectsForClone call is removed — the cloner does it
    /// automatically via Pass 5.
    /// </summary>
    [Fact]
    public void Spike_ClonedAnthem_BuffsCreaturesInSandbox()
    {
        var (alice, _, chieftain, goblinWarrior, liveCes) = BuildLiveAnthemBoard();

        // Sanity: live board is correct.
        goblinWarrior.Power.Should().Be(3, "live: 2/2 + Chieftain +1/+1");

        // Clone — no manual RebuildLordEffectsForClone needed; cloner does it.
        var cloned = GameStateCloner.Clone(new[] { alice, new Player("Bob", 20) });
        var clonedChieftain = (Creature)cloned.CardMap[chieftain.InstanceId];
        var clonedGoblinWarrior = (Creature)cloned.CardMap[goblinWarrior.InstanceId];

        // GOAL (now production): anthem re-applies in the sandbox.
        clonedGoblinWarrior.ActiveEffects.Should().NotBeNull(
            "Pass 5 assigns a fresh CES to cloned battlefield permanents");
        clonedGoblinWarrior.Power.Should().Be(3,
            "Goblin Warrior gets +1/+1 from the re-registered Chieftain lord");
        clonedGoblinWarrior.Toughness.Should().Be(3);

        // Chieftain's own P/T is unaffected (includeSelf: false).
        clonedChieftain.Power.Should().Be(2, "Chieftain is 2/2 — its own lord doesn't buff itself");
        clonedChieftain.Toughness.Should().Be(2);
    }

    // ── Board builder ────────────────────────────────────────────────────────

    private static (Player alice, Player bob, Creature chieftain, Creature goblinWarrior, ContinuousEffectsService ces)
        BuildLiveAnthemBoard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Shared ContinuousEffectsService — the "live" game's layer system.
        var ces = new ContinuousEffectsService();

        // Goblin Chieftain: 2/2 Goblin Warrior — registers a LordStaticEffect:
        // "Other Goblin creatures you control have haste and get +1/+1."
        var chieftain = GoblinChieftainFactory.Create(alice, ces);
        chieftain.ChangeOwner(alice);
        chieftain.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(chieftain);   // sets Zone = Battlefield
        chieftain.ActiveEffects = ces;                // wire so lord IsActive() works

        // A plain Goblin Warrior (2/2) that will be buffed by the Chieftain lord.
        // We construct inline so no factory dispatch is involved.
        var goblinWarrior = new Creature("Goblin Warrior", "{R}", 2, 2,
            subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Goblin, Majik.Core.Cards.Types.CardSubtype.Warrior });
        goblinWarrior.ChangeOwner(alice);
        goblinWarrior.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(goblinWarrior); // sets Zone = Battlefield
        goblinWarrior.ActiveEffects = ces;              // wire so Compute sees the CES

        return (alice, bob, chieftain, goblinWarrior, ces);
    }

}
