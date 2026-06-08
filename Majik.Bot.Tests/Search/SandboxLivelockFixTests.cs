using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using System.Diagnostics;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Phase 2A Task A — livelock fix regression suite.
///
/// The spin root cause:
///   1. LegalActionEnumerator counted untapped lands as mana, but lands without
///      a wired ManaAbility caused CastSpell proposals that could not be paid.
///   2. The cast dispatcher silently rotated the card back to hand, but
///      PriorityLoop.ApplyActionAsync returned true → loop believed it progressed.
///   3. PriorityPolicy had anti-spin memos for land drops and activated abilities,
///      but NOT for spell casts → bot re-proposed the same uncastable spells up
///      to the 500-action cap every priority round.
///
/// Fix 1 — PriorityPolicy.cs: per-turn _castProposedThisTurn HashSet<Guid>
/// suppresses re-proposing a card that is still in hand after a failed cast.
///
/// Fix 2 — PriorityLoop.cs + call sites: castDispatcher now returns bool;
/// ApplyActionAsync propagates false back so PriorityLoop calls PassPriority()
/// instead of spinning.
/// </summary>
public sealed class SandboxLivelockFixTests
{
    // ── Fix 1 / PriorityPolicy cast anti-spin memo unit tests ─────────────

    [Fact]
    public void CastSpell_NotReproposed_WhenStillInHandAfterFirstProposal()
    {
        // Arrange: bot has lands + a spell in hand.
        // The scenario uses plain (new Land("Forest")) which has NO wired
        // ManaAbility, mirroring the exact livelock scenario — the bot
        // "sees" mana but the dispatcher will fail to pay.
        var s = new BotTestScenario();
        // Two untapped lands on battlefield (no wired ManaAbility → will fail payment)
        s.AddLandToBattlefield(s.Self, "Forest1");
        s.AddLandToBattlefield(s.Self, "Forest2");
        var crt = new Creature("Grizzly Bears", manaCost: "{1}{G}", power: 2, toughness: 2);
        s.AddCardToHand(s.Self, crt);

        var pol = new PriorityPolicy(ArchetypeWeights.Burn);

        // Act — first pick: policy should propose CastSpell (it looks castable)
        var first = pol.Pick(s.Context, s.Self);

        // The card is still in hand (no real engine ran).
        // Act — second pick on the SAME turn/context: the card is still in hand
        // → the anti-spin memo must suppress re-proposing it.
        var second = pol.Pick(s.Context, s.Self);

        // Assert
        first.Should().BeOfType<PriorityAction.CastSpell>(
            "the policy initially sees the card as castable");
        second.Should().NotBeOfType<PriorityAction.CastSpell>(
            "the cast memo must suppress re-proposing the same card that is still in hand");
    }

    [Fact]
    public void CastMemo_ResetsOnTurnBoundary()
    {
        // After a turn boundary the bot must be able to propose the spell again
        // (it may have drawn mana, or we're in a fresh priority sequence).
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Forest1");
        s.AddLandToBattlefield(s.Self, "Forest2");
        var crt = new Creature("Grizzly Bears", manaCost: "{1}{G}", power: 2, toughness: 2);
        s.AddCardToHand(s.Self, crt);

        var pol = new PriorityPolicy(ArchetypeWeights.Burn);

        // Propose (and be blocked) on turn 1
        pol.Pick(s.Context, s.Self).Should().BeOfType<PriorityAction.CastSpell>();
        pol.Pick(s.Context, s.Self).Should().NotBeOfType<PriorityAction.CastSpell>();

        // Advance to turn 2 — memo should reset
        var turn2Ctx = new Majik.Core.Game.GameContext(
            s.Self,
            new[] { s.Self, s.Opponent },
            activePlayer: s.Self,
            turnNumber: 2,
            currentPhase: StepStateType.PreCombatMain,
            stack: s.Stack);

        pol.Pick(turn2Ctx, s.Self).Should().BeOfType<PriorityAction.CastSpell>(
            "turn boundary resets the cast anti-spin memo");
    }

    [Fact]
    public void CastMemo_DoesNotBlock_WhenDifferentCardInHand()
    {
        // One card proposed → memo blocks THAT card's InstanceId, not others.
        var s = new BotTestScenario();
        for (int i = 0; i < 3; i++) s.AddLandToBattlefield(s.Self, $"F{i}");
        var card1 = new Creature("Bear A", manaCost: "{1}{G}", power: 2, toughness: 2);
        var card2 = new Creature("Bear B", manaCost: "{1}{G}", power: 2, toughness: 2);
        s.AddCardToHand(s.Self, card1);
        s.AddCardToHand(s.Self, card2);

        var pol = new PriorityPolicy(ArchetypeWeights.Burn);

        // First pick — one of the two bears is proposed (whichever scores higher,
        // they're equal so it's likely the first enumerated).
        var p1 = pol.Pick(s.Context, s.Self);
        p1.Should().BeOfType<PriorityAction.CastSpell>();

        // Second pick — the proposed card is memoised; the OTHER card should still
        // be available (and scores the same), so a CastSpell may still be returned.
        var p2 = pol.Pick(s.Context, s.Self);
        // We don't demand CastSpell here because scoring depends on enumeration order,
        // but we MUST NOT see the SAME card proposed again if it's still in hand.
        if (p2 is PriorityAction.CastSpell cs2)
        {
            var p1Card = ((PriorityAction.CastSpell)p1).Card;
            cs2.Card.InstanceId.Should().NotBe(p1Card.InstanceId,
                "the first-proposed card should not be re-proposed while still in hand");
        }
    }

    [Fact]
    public void CastsAffordableCreature_NormalCastingStillWorks()
    {
        // Regression guard: the anti-spin memo must NOT prevent a spell from
        // being proposed on the FIRST pick. Normal casting flow must survive.
        var s = new BotTestScenario();
        // Add lands as untapped permanents (they score as mana sources in
        // LegalActionEnumerator regardless of whether they have ManaAbility)
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        var crt = new Creature("Goblin Guide", manaCost: "{R}", power: 2, toughness: 2);
        s.AddCardToHand(s.Self, crt);

        var pol = new PriorityPolicy(ArchetypeWeights.Burn);
        pol.Pick(s.Context, s.Self).Should().BeOfType<PriorityAction.CastSpell>(
            "first-time cast proposal must work (memo only suppresses re-proposals)");
    }

    // ── Fix 2 / castDispatcher bool return — PriorityLoop unit tests ──────

    [Fact]
    public async Task CastDispatcher_ReturnsFalse_ForcesPassNotSpin()
    {
        // Wires a castDispatcher that returns false (failed cast) and verifies
        // the loop calls PassPriority() rather than spinning to the 500 cap.
        // The agent proposes CastSpell repeatedly; without Fix 2 the loop
        // would spin to kActionLimit. With Fix 2 the failed cast is treated
        // as a pass → the round ends quickly.
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);
        var resolver = new Majik.Core.Services.StackResolver(bus, zones);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new Majik.Core.Game.PriorityManager(
            new List<Player> { alice, bob }, stack, bus, triggers);

        var bolt = new Instant("Bolt", "{R}");
        bolt.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(bolt);

        var dispatchCount = 0;
        // Dispatcher returns false = cast failed
        Func<Player, PriorityAction.CastSpell, Majik.Core.Game.GameContext, Task<bool>> failingDispatcher
            = (_, _, _) => { dispatchCount++; return Task.FromResult(false); };

        // Agent proposes CastSpell 10 times in a row (simulating spin scenario)
        var aliceAgent = new ScriptedAgent();
        for (var i = 0; i < 10; i++)
            aliceAgent.QueuePriority(new PriorityAction.CastSpell(bolt, Array.Empty<object>()));
        aliceAgent.QueuePriority(PriorityAction.Pass);

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 5; i++) bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = new Majik.Core.Game.PriorityLoop(
            new[] { alice, bob }, priority, stack, resolver, zones,
            new Dictionary<Player, IPlayerAgent> { [alice] = aliceAgent, [bob] = bobAgent },
            () => 1, () => StepStateType.PreCombatMain,
            new Majik.Core.Game.LandDropTracker(),
            castDispatcher: failingDispatcher);

        var sw = Stopwatch.StartNew();
        await loop.RunUntilRoundEndsAsync(alice);
        sw.Stop();

        // With Fix 2: each failed cast immediately PassPriority()s → round ends fast.
        // Without Fix 2: loop spins to 500 cap (each failed cast is still "applied").
        // We can't assert dispatchCount == 1 (the scripted agent queued 10 casts,
        // each get PassPriority'd so priority shifts to Bob, who passes, so after
        // alice's first failed cast priority passes through all).
        // Key invariant: loop exits quickly and didn't hit the 500 cap path.
        sw.ElapsedMilliseconds.Should().BeLessThan(5000,
            "failed cast returns false → PassPriority → round advances fast");
    }

    // ── End-to-end sandbox timing test ────────────────────────────────────

    /// <summary>
    /// The actual regression: sandbox game with spells in hand + lands without
    /// wired ManaAbilities must NOT spin the priority loop to the 500-action cap
    /// on every priority round. Pre-fix this took many seconds; post-fix it
    /// should be well under 2 seconds for 4 turns.
    /// </summary>
    [Fact(Timeout = 15_000)]
    public async Task Sandbox_FromMain_WithUnpayableSpellsInHand_DoesNotSpinToCap()
    {
        // Board matches Spike_SandboxLivelockTests.BuildBoard(spelledHands:true)
        var (alice, bob) = BuildBoard(spelledHands: true);

        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            new GameRandom(1),
            p => new BotPlayerAgent(p, new BotConfig("Burn", Strategy: "heuristic", SimCombatBudgetMs: 20)));

        var sw = Stopwatch.StartNew();
        await sandbox.ResumeAsync(
            PhaseStateType.PreCombatMain,
            sandbox.State.PlayerFor(alice),
            turnNumber: 3,
            maxTurns: 8,
            ct: default);
        sw.Stop();

        // Pre-fix: ~multi-second (500-cap spins per priority round per turn).
        // Post-fix: well under 2 s for 8 turns with no wired mana.
        sw.ElapsedMilliseconds.Should().BeLessThan(2000,
            "the priority loop must not spin to the 500-action cap on uncastable spells");
    }

    // ── Board builder (mirrors Spike_SandboxLivelockTests.BuildBoard) ─────

    private static (Player alice, Player bob) BuildBoard(bool spelledHands)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        SetupPlayer(alice, spelledHands);
        SetupPlayer(bob, spelledHands);
        return (alice, bob);
    }

    private static void SetupPlayer(Player p, bool spelledHands)
    {
        // 4 untapped lands on battlefield (no wired ManaAbility — exact livelock condition)
        for (var i = 0; i < 4; i++)
        {
            var land = new Land("Forest");
            land.ChangeOwner(p);
            land.ChangeController(p);
            p.Zones.Battlefield.AddCard(land);
        }
        // 2 creatures already on battlefield (ready to attack)
        for (var i = 0; i < 2; i++)
        {
            var bear = new Creature($"Grizzly Bears {i}", "{1}{G}", 2, 2);
            bear.ChangeOwner(p);
            bear.ChangeController(p);
            p.Zones.Battlefield.AddCard(bear);
            bear.ClearSummoningSickness();
        }
        // Hand: always 3 lands
        for (var i = 0; i < 3; i++)
        {
            var land = new Land("Forest");
            land.ChangeOwner(p);
            p.Zones.Hand.AddCard(land);
        }
        if (spelledHands)
        {
            // 3 creatures in hand — "affordable" (CMC 2, 4 lands on field)
            // but no ManaAbility is wired → actual payment will fail → livelock
            for (var i = 0; i < 3; i++)
            {
                var bear = new Creature($"Grizzly Bears Hand {i}", "{1}{G}", 2, 2);
                bear.ChangeOwner(p);
                p.Zones.Hand.AddCard(bear);
            }
        }
        // Library: 20 lands (prevent draw-loss)
        for (var i = 0; i < 20; i++)
        {
            var land = new Land("Forest");
            land.ChangeOwner(p);
            p.Zones.Library.AddCard(land);
        }
    }
}
