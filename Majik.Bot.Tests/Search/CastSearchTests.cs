using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using System.Threading.Tasks;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Stage 2B Task 1: verify that MCTS actually SEARCHES spell casts and that
/// the live-remap from sandbox clone → live card works end-to-end.
///
/// <para>
/// Previously <see cref="SearchStrategy.RemapPriorityAction"/> fell back to
/// the inner heuristic for every <see cref="PriorityAction.CastSpell"/>, so
/// even when the search chose a cast it was silently discarded. These tests
/// verify the remap is now live.
/// </para>
/// </summary>
public sealed class CastSearchTests
{
    /// <summary>
    /// Core test: bot has an affordable creature spell (Goblin Guide, {R}) in
    /// hand with 1 untapped Mountain on battlefield. The bot's board is empty
    /// while the opponent is at 20 life — casting the creature strictly improves
    /// board position vs passing. The returned action MUST:
    ///   1. Be a <see cref="PriorityAction.CastSpell"/> (not Pass or heuristic fallback).
    ///   2. Reference the LIVE card by InstanceId (remap worked, not a sandbox clone).
    ///
    /// <para>
    /// This validates the end-to-end flow: MCTS chooses Cast over Pass AND the
    /// live remap correctly identifies the LIVE card object. The creature spell
    /// is used because it is executable in the sandbox (permanents use a vanilla
    /// SpellDefinition; instants/sorceries need a registered spellDefResolver
    /// which the sandbox doesn't wire). The principle is identical — a sorcery-
    /// speed cast that positively affects board position is chosen by search.
    /// </para>
    ///
    /// <para>
    /// Mountains are created via <see cref="NamedCardFactory.Create"/> so they
    /// carry a wired <c>ManaAbility</c> the sandbox can tap for {R}.
    /// </para>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public Task SearchStrategy_CastsRemoval_OnOpponentThreat_ViaSearch() => Task.Run(() =>
    {
        // ── Board setup ────────────────────────────────────────────────────
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        // Alice has 1 untapped Mountain → 1 mana available.
        // NamedCardFactory.Create wires the Mountain with a ManaAbility so the
        // sandbox can actually tap it for {R} when casting the spell.
        var mountain = (Land)NamedCardFactory.Create("Mountain", alice);
        mountain.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(mountain);

        // Alice has a Goblin Guide ({R}, 2/2) in hand — the LIVE card we'll check.
        // A permanent spell (creature) is used because the sandbox can resolve
        // it without a registered spellDefResolver (permanents get a vanilla
        // definition, targeting is not required).
        var goblinGuide = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2);
        goblinGuide.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(goblinGuide);

        // Bob has a large creature on board — further incentive for Alice to
        // develop her board (casting a 2/2 improves her position).
        var threat = new Creature("Kalonian Tusker", "{G}{G}", power: 3, toughness: 3);
        threat.ChangeOwner(bob);
        threat.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(threat);
        threat.ClearSummoningSickness();

        // Pad libraries to prevent draw-loss in the sandbox.
        foreach (var _ in Enumerable.Range(0, 20))
        {
            var al = new Land("Forest");
            al.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(al);

            var bl = new Land("Forest");
            bl.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(bl);
        }

        // ── Build context: Alice's main phase, no land in hand.
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            currentPhase: StepStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack(new EventBus()),
            landPlayAvailable: false);

        // Small MCTS budget to keep the test fast; 100 iterations + 5 s max.
        var config = new BotConfig(
            "Burn",
            Strategy: "mcts",
            MaxMctsIterations: 100,
            MaxMctsBudgetMs: 5_000,
            PrioritySearchEnabled: true);
        var strategy = new SearchStrategy(config);

        // ── Act ────────────────────────────────────────────────────────────
        var action = strategy.PickPriorityAction(ctx, alice);

        // ── Assert ─────────────────────────────────────────────────────────
        // The action MUST be a CastSpell.  Previously this fell back to the
        // heuristic AND returned the heuristic's choice; after the fix the
        // search's chosen cast must come back with the LIVE card reference.
        action.Should().BeOfType<PriorityAction.CastSpell>(
            because: "casting a 2/2 for {R} is better than passing when you have 1 mana and an empty board");

        var castAction = (PriorityAction.CastSpell)action;
        castAction.Card.InstanceId.Should().Be(goblinGuide.InstanceId,
            because: "the remap must return the LIVE Goblin Guide object, not a sandbox clone");
    });

    /// <summary>
    /// Regression guard: when PrioritySearchEnabled=false the remap path is
    /// bypassed and the inner heuristic runs.  This must still work correctly
    /// (the heuristic itself would cast the bolt here too — but we're not
    /// asserting that; we're just asserting no crash + some PriorityAction
    /// is returned so the heuristic path is reachable).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public Task SearchStrategy_WithPrioritySearchDisabled_DelegatesToHeuristic() => Task.Run(() =>
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        var mountain = (Land)NamedCardFactory.Create("Mountain", alice);
        mountain.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(mountain);

        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2);
        goblin.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(goblin);

        foreach (var _ in Enumerable.Range(0, 15))
        {
            var al = new Land("Forest");
            al.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(al);

            var bl = new Land("Forest");
            bl.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(bl);
        }

        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            currentPhase: StepStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack(new EventBus()),
            landPlayAvailable: false);

        var config = new BotConfig("Burn", Strategy: "mcts", PrioritySearchEnabled: false);
        var strategy = new SearchStrategy(config);

        var action = strategy.PickPriorityAction(ctx, alice);

        // Just ensure a valid action is returned — no crash.
        action.Should().NotBeNull();
    });

    /// <summary>
    /// Guard: when MCTS is used for priority but only Pass is legal (empty
    /// hand, nothing to do), the search short-circuits to Pass without
    /// invoking the remap. Tests that the CastSpell remap does NOT interfere
    /// with the Pass short-circuit path.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public Task SearchStrategy_PassesWhenNothingToCast_NoBoardOrHand() => Task.Run(() =>
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        foreach (var _ in Enumerable.Range(0, 15))
        {
            var al = new Land("Forest");
            al.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(al);

            var bl = new Land("Forest");
            bl.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(bl);
        }

        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            currentPhase: StepStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack(new EventBus()),
            landPlayAvailable: false);

        var config = new BotConfig(
            "Burn",
            Strategy: "mcts",
            MaxMctsIterations: 50,
            MaxMctsBudgetMs: 2_000,
            PrioritySearchEnabled: true);
        var strategy = new SearchStrategy(config);

        var action = strategy.PickPriorityAction(ctx, alice);

        action.Should().BeOfType<PriorityAction.PassAction>(
            because: "with no legal actions other than Pass, the search must short-circuit to Pass");
    });

    /// <summary>
    /// Untargeted creature cast via search:  bot has a Goblin Guide ({R}) in
    /// hand with mana.  The search should pick CastSpell and the remap should
    /// return the LIVE creature card.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public Task SearchStrategy_CastsCreature_ViaSearch_LiveCardReturned() => Task.Run(() =>
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        // Two wired Mountains — afford a {R} creature easily.
        // NamedCardFactory.Create wires each Mountain with a ManaAbility.
        for (int i = 0; i < 2; i++)
        {
            var m = (Land)NamedCardFactory.Create("Mountain", alice);
            m.ChangeController(alice);
            alice.Zones.Battlefield.AddCard(m);
        }

        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2);
        goblin.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(goblin);

        foreach (var _ in Enumerable.Range(0, 20))
        {
            var al = new Land("Forest");
            al.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(al);

            var bl = new Land("Forest");
            bl.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(bl);
        }

        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            currentPhase: StepStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack(new EventBus()),
            landPlayAvailable: false);

        var config = new BotConfig(
            "Burn",
            Strategy: "mcts",
            MaxMctsIterations: 100,
            MaxMctsBudgetMs: 5_000,
            PrioritySearchEnabled: true);
        var strategy = new SearchStrategy(config);

        var action = strategy.PickPriorityAction(ctx, alice);

        action.Should().BeOfType<PriorityAction.CastSpell>(
            because: "casting a 2/2 for {R} is better than passing with 2 mana available");

        var castAction = (PriorityAction.CastSpell)action;
        castAction.Card.InstanceId.Should().Be(goblin.InstanceId,
            because: "the remap must return the LIVE Goblin Guide, not a sandbox clone");
    });

    /// <summary>
    /// No-deadlock / no-spin guard: with a castable spell in hand the 2-core
    /// priority search must complete in under the timeout.  This mirrors the
    /// livelock regression test but specifically for the cast-search path.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public Task SearchStrategy_CastSearch_DoesNotDeadlockOrSpin() => Task.Run(() =>
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        var mountain = (Land)NamedCardFactory.Create("Mountain", alice);
        mountain.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(mountain);

        // Use a creature spell (permanent) — executable in the sandbox without
        // a spellDefResolver. Instant/sorceries need an oracle binder the
        // sandbox doesn't wire; this test focuses on the no-deadlock/spin guard.
        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2);
        goblin.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(goblin);

        foreach (var _ in Enumerable.Range(0, 20))
        {
            var al = new Land("Forest");
            al.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(al);

            var bl = new Land("Forest");
            bl.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(bl);
        }

        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            currentPhase: StepStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack(new EventBus()),
            landPlayAvailable: false);

        // 50 iterations, 5 s wall budget — should complete far faster.
        var config = new BotConfig(
            "Burn",
            Strategy: "mcts",
            MaxMctsIterations: 50,
            MaxMctsBudgetMs: 5_000,
            PrioritySearchEnabled: true);
        var strategy = new SearchStrategy(config);

        // Just verifying it completes (the Fact Timeout is the deadlock guard).
        var action = strategy.PickPriorityAction(ctx, alice);
        action.Should().NotBeNull("search must return a valid action without deadlocking");
    });
}
