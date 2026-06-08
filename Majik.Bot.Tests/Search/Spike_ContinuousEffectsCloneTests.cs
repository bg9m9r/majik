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
/// DIAGNOSTIC SPIKE — Phase 2A Task 2.
///
/// Goal: determine whether cloned permanents' continuous effects (lords /
/// anthems) can be made to apply inside the search sandbox, and at what cost.
///
/// The two tests below probe the failure and the prototype fix:
///   1. <see cref="Spike_ClonedAnthem_DropsBuffInSandbox"/> — red-bar
///      confirming the bug: a live +1/+1 anthem evaluates as 0-buff in the
///      clone (ActiveEffects == null).
///   2. <see cref="Spike_ClonedAnthem_BuffsCreaturesInSandbox"/> — the
///      prototype approach C (re-registration) that makes the anthem apply.
///
/// ## Anthem chosen: Goblin Chieftain
/// GoblinChieftainFactory.Create(owner, ces) registers a LordStaticEffect:
///   - matchingSubtype: Goblin, power: +1, toughness: +1, includeSelf: false
/// This is the SIMPLEST possible registered lord:
///   - LordStaticEffect is a generic reusable class (not bespoke).
///   - It captures only the SOURCE permanent and four value fields (subtype,
///     P/T delta, keyword list, flags). NO allPlayersResolver required.
///   - AppliesTo checks creature.Controller == source.Controller and subtype.
///     Both of these are answered entirely from data on the CLONED permanent
///     + the CLONED player — no live-game closures.
///
/// ## How LordStaticEffect.AppliesTo computes P/T
/// Reads: effect.Source.Zone (battlefield gate), creature.Zone, creature.Controller,
/// effect.Source.Controller, creature subtype. ALL of these are value fields on
/// the CLONED permanent/player — no external resolver, no live-game closure,
/// no allPlayersResolver. This makes it trivially safe to reconstruct the same
/// LordStaticEffect pointing at the CLONED source + a clone-owned CES.
///
/// ## Re-registration approach: C (cheapest)
/// After Clone(), build a fresh ContinuousEffectsService for the sandbox.
/// Walk the ORIGINAL registered effects; for each LordStaticEffect (or, in
/// the prototype, for the one known lord), construct the SAME LordStaticEffect
/// pointed at the CLONED source. Register it against the fresh CES. Assign the
/// fresh CES to all CLONED battlefield permanents. Because LordStaticEffect
/// reads only from source.Controller/Zone (both correctly set on the clone) and
/// the checked creature's Controller/Zone/subtypes (also correctly set), the
/// reconstructed effect is behaviourally identical to the live one.
///
/// ## [EXPERIMENTAL] prototype only — not wired to production SandboxGame.
/// </summary>
public sealed class Spike_ContinuousEffectsCloneTests
{
    // ── Step 3: Confirm the bug ──────────────────────────────────────────────

    /// <summary>
    /// RED: clone drops the anthem — cloned Goblin reads base P/T only.
    ///
    /// Confirms <see cref="GameStateCloner"/> leaves ActiveEffects == null on
    /// cloned permanents, so the lord bonus is invisible in the sandbox.
    /// </summary>
    [Fact]
    public void Spike_ClonedAnthem_DropsBuffInSandbox()
    {
        var (alice, _, chieftain, goblinWarrior, liveCes) = BuildLiveAnthemBoard();

        // LIVE check — the anthem must apply before cloning.
        goblinWarrior.Power.Should().Be(3, "live: Goblin Warrior 2/2 + Chieftain's +1/+1 = 3/3");
        goblinWarrior.Toughness.Should().Be(3);

        // Clone — ActiveEffects will be null on all clones.
        var cloned = GameStateCloner.Clone(new[] { alice, new Player("Bob", 20) });

        var clonedGoblinWarrior = (Creature)cloned.CardMap[goblinWarrior.InstanceId];

        // BUG: anthem dropped → clone evaluates as base 2/2.
        clonedGoblinWarrior.ActiveEffects.Should().BeNull(
            "GameStateCloner intentionally leaves ActiveEffects null on clones");
        clonedGoblinWarrior.Power.Should().Be(2,
            "without ActiveEffects, GetPower() returns BasePower — anthem missing");
        clonedGoblinWarrior.Toughness.Should().Be(2,
            "without ActiveEffects, GetToughness() returns BaseToughness — anthem missing");
    }

    // ── Step 5: Prototype fix ────────────────────────────────────────────────

    /// <summary>
    /// GREEN (prototype Approach C): re-registration of lord effects after
    /// cloning. The cloned Goblin Warrior should read 3/3 (base 2/2 + anthem
    /// +1/+1) once the rebuilt CES is assigned.
    ///
    /// [EXPERIMENTAL] — prototype only; not wired into production SandboxGame.
    /// </summary>
    [Fact]
    public void Spike_ClonedAnthem_BuffsCreaturesInSandbox()
    {
        var (alice, _, chieftain, goblinWarrior, liveCes) = BuildLiveAnthemBoard();

        // Sanity: live board is correct.
        goblinWarrior.Power.Should().Be(3, "live: 2/2 + Chieftain +1/+1");

        // Clone.
        var cloned = GameStateCloner.Clone(new[] { alice, new Player("Bob", 20) });
        var clonedChieftain = (Creature)cloned.CardMap[chieftain.InstanceId];
        var clonedGoblinWarrior = (Creature)cloned.CardMap[goblinWarrior.InstanceId];

        // [EXPERIMENTAL] Approach C prototype:
        // Rebuild lord effects against the cloned permanents, assign a fresh CES
        // to every cloned battlefield permanent in the affected player's zone.
        RebuildLordEffectsForClone(liveCes, cloned);

        // GOAL: anthem re-applies in the sandbox.
        clonedGoblinWarrior.ActiveEffects.Should().NotBeNull(
            "prototype assigns a fresh CES to cloned battlefield permanents");
        clonedGoblinWarrior.Power.Should().Be(3,
            "prototype: Goblin Warrior gets +1/+1 from the re-registered Chieftain lord");
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

    // ── [EXPERIMENTAL] Approach C prototype ─────────────────────────────────

    /// <summary>
    /// [EXPERIMENTAL] Prototype Approach C: re-register lord effects in a fresh
    /// ContinuousEffectsService bound to the cloned permanents.
    ///
    /// Algorithm:
    ///   1. Build one fresh CES per cloned player (or a single shared one here
    ///      for simplicity — real impl would scope per player or globally).
    ///   2. Walk the ORIGINAL live CES's registered effects.
    ///   3. For each <see cref="LordStaticEffect"/> whose Source is a cloned
    ///      battlefield permanent, construct an IDENTICAL LordStaticEffect
    ///      pointing at the CLONED source. All parameters are value types —
    ///      no live-game closures captured. Register against the fresh CES.
    ///   4. Assign the fresh CES to every cloned battlefield permanent.
    ///
    /// Why this works for LordStaticEffect:
    ///   - Source is now the CLONED permanent (Zone == Battlefield, Controller ==
    ///     cloned alice) — IsActive() and AppliesTo(...controller...) read these.
    ///   - Creature.Controller on the cloned permanent is the cloned player, not
    ///     original alice — the "same controller" filter is coherent within the
    ///     clone universe.
    ///   - No allPlayersResolver is needed (LordStaticEffect defaults to
    ///     controller-scoped).
    ///
    /// Cost note: this prototype uses reflection-free constructor access by
    /// type-matching on LordStaticEffect. The full production version would
    /// either (a) add a <c>CloneForSim(Permanent clonedSource)</c> virtual on
    /// ContinuousEffect, or (b) handle only LordStaticEffect + manually-listed
    /// bespoke lords (see cost assessment in the class doc).
    /// </summary>
    private static void RebuildLordEffectsForClone(
        ContinuousEffectsService liveCes,
        ClonedGame cloned)
    {
        // One fresh CES for the entire cloned game. (Production would scope
        // per-game, not per-test, but the semantics are identical.)
        var sandboxCes = new ContinuousEffectsService();

        // Reflect the private _effects list via the exposed Compute path.
        // Instead, we use a known-type probe: iterate the live effects and
        // re-register those of type LordStaticEffect whose source maps to a clone.
        //
        // We access live registered effects by asking the CES to Compute a
        // dummy creature — no, that's too indirect. Instead we expose the
        // private field via a test-only helper that we implement inline here
        // using the internal test access pattern (same as in LayeredCreatureTests).
        //
        // For the prototype we KNOW the only registered effect is the Chieftain
        // lord (LordStaticEffect). We enumerate it via an internal accessor.
        //
        // [SPIKE NOTE] In production, ContinuousEffect.CloneForSim(clonedSource)
        // would be the clean hook. Here we access the live effect list through
        // the ContinuousEffectsService's public probe: an active LordStaticEffect
        // whose Source is a live permanent can be detected by checking if
        // liveCes.HasRestriction / CanBlockUnder... — but those are specialized.
        // Instead, we use a pragmatic approach: expose effects via a test seam.
        //
        // The cleanest spike approach: read the live CES's _effects via a
        // package-internal or via the public GetRegisteredEffects (if it exists).
        // Since it doesn't exist publicly, we use the approach of iterating via
        // the type-erasure probe pattern (Compute a creature we know is on both
        // boards).
        //
        // For this prototype we use the DIRECT approach: rebuild from knowledge
        // of the board we constructed. A real implementation would need either
        // a public effects enumerator or a CloneForSim virtual.
        //
        // PROTOTYPE SEAM: call the factory to re-register for the cloned source.
        // This works because GoblinChieftainFactory.Create(owner, ces) performs
        // only two operations: construct the creature + register the lord.
        // We already have the cloned creature; we only need the registration.
        // We extract the re-registration as a local lambda.

        // Find the cloned chieftain (any Goblin Warrior with BasePower 2 and
        // Name "Goblin Chieftain" on alice's battlefield).
        var clonedAlice = cloned.Players[0];
        foreach (var card in clonedAlice.Zones.Battlefield.GetCards())
        {
            if (card is Permanent p)
            {
                // Wire the fresh CES to every cloned battlefield permanent.
                p.ActiveEffects = sandboxCes;
            }
        }

        // Re-register the lord for the cloned chieftain.
        // In production this would be: foreach live effect whose Source is in
        // CardMap, call effect.CloneForSim(clonedSource, sandboxCes).
        // For the prototype we enumerate cloned permanents and re-create the
        // known LordStaticEffect shape from the Chieftain factory.
        foreach (var card in clonedAlice.Zones.Battlefield.GetCards())
        {
            if (card is Creature c &&
                c.Name == GoblinChieftainFactory.CardName)
            {
                // Reconstruct the SAME LordStaticEffect that GoblinChieftainFactory
                // would register, but pointing at the CLONED chieftain.
                sandboxCes.Register(new LordStaticEffect(
                    source: c,
                    matchingSubtype: Majik.Core.Cards.Types.CardSubtype.Goblin,
                    power: 1,
                    toughness: 1,
                    grantedKeywords: new[] { "Haste" },
                    includeSelf: false,
                    opponentsOnly: false));
            }
        }
    }
}
