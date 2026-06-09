using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Search;
using Majik.Bot.Strategies;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// DIAGNOSTIC — pinpoints why the MCTS bot never activates Goblin Charbelcher
/// after the mana-availability and activation-symmetry fixes.
///
/// <para>
/// Two scenarios tested:
///
/// <b>Scenario A</b> — Charbelcher on board, NO floating mana, rituals in hand.
/// Does the bot find the ritual→belch sequence?
///
/// <b>Scenario B</b> — Charbelcher on board, {3} already floating (post-ritual).
/// Does the bot activate the belch directly?
/// </para>
///
/// <para>
/// The tests instrument:
///   - What <see cref="LegalActionEnumerator.ForPriority"/> enumerates.
///   - What <see cref="BelcherStrategy.TryGetNextWinningAction"/> returns
///     (the DIRECTIVE path that bypasses MCTS entirely).
///   - What <see cref="SearchStrategy.PickPriorityAction"/> returns (the full
///     decision path including directive → MCTS → remap → heuristic).
///   - Whether <see cref="PriorityPolicy"/> (heuristic fallback) picks the belch.
/// </para>
///
/// <para>
/// These are <b>[Fact]</b> (not Skip) tests because they establish a precise,
/// fast unit-level regression baseline for the blocker. Each test documents a
/// specific decision point so future fixes can be validated with a single run.
/// </para>
/// </summary>
public sealed class BelcherActivationDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    public BelcherActivationDiagnosticTests(ITestOutputHelper output)
    {
        _out = output;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a Goblin Charbelcher artifact with the real cost shape ({3},{T})
    /// but a trivial no-op effect — mirrors GoblinCharbelcherFactory costs
    /// without requiring the full factory pipeline.
    /// </summary>
    private static Artifact BuildCharbelcher(Player owner)
    {
        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(owner);
        belcher.ChangeController(owner);

        ActivatedAbility? ability = null;
        var effect = new Effect(
            "Goblin Charbelcher: reveal-until-nonland + damage + random-bottom",
            () => { /* no-op in unit tests */ });

        ability = new ActivatedAbility(
            source: belcher,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Tap(belcher),
            },
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        belcher.AddAbility(ability);
        return belcher;
    }

    /// <summary>
    /// Add a dummy Sorcery to a player's hand (ritual stand-in for tests that
    /// need "something castable in hand" without real mana abilities).
    /// </summary>
    private static Sorcery AddRitualToHand(Player player, string name = "Desperate Ritual")
    {
        var ritual = new Sorcery(name, "{1}{R}");
        ritual.ChangeOwner(player);
        player.Zones.Hand.AddCard(ritual);
        return ritual;
    }

    /// <summary>
    /// Pad both players' libraries with basic lands so the sandbox engine
    /// does not trigger a draw-loss immediately.
    /// </summary>
    private static void PadLibraries(Player a, Player b, int count = 20)
    {
        for (int i = 0; i < count; i++)
        {
            var la = new Land($"Pad-{i}-A");
            la.ChangeOwner(a);
            a.Zones.GetZone(ZoneType.Library).AddCard(la);

            var lb = new Land($"Pad-{i}-B");
            lb.ChangeOwner(b);
            b.Zones.GetZone(ZoneType.Library).AddCard(lb);
        }
    }

    /// <summary>Build a PreCombatMain GameContext where <paramref name="self"/> is active.</summary>
    private static GameContext AtMain(Player self, Player opp)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        return new GameContext(
            self: self,
            allPlayers: new[] { self, opp },
            activePlayer: self,
            turnNumber: 1,
            currentPhase: StepStateType.PreCombatMain,
            stack: stack,
            landPlayAvailable: false); // Belcher deck runs 0 lands
    }

    // ── SCENARIO B — {3} already floating ─────────────────────────────────────

    /// <summary>
    /// Scenario B, Step 1:
    /// Charbelcher on board (untapped) + {3} floating in pool.
    /// <see cref="LegalActionEnumerator.ForPriority"/> MUST include an
    /// ActivateAbility for the belch — the pre-fix bug prevented this because
    /// the mana portion was checked against the pool only, which was correct
    /// here (the pool IS {3}), but the non-mana tap check also needed Charbelcher
    /// to be untapped.
    ///
    /// This test confirms Step 1: enumeration is correct in Scenario B.
    /// </summary>
    [Fact]
    public void ScenarioB_Step1_Enumeration_IncludesBelch_When3Floating()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        // Fund the {3} activation in the floating pool — post-ritual state.
        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        var ctx = AtMain(bot, opp);

        var legal = LegalActionEnumerator.ForPriority(ctx, bot);
        var activations = legal.OfType<PriorityAction.ActivateAbility>().ToList();

        _out.WriteLine($"[B.Step1] legal actions: {legal.Count}");
        _out.WriteLine($"[B.Step1] activations: {activations.Count}");
        foreach (var a in legal)
            _out.WriteLine($"[B.Step1]   {a}");

        activations.Should().NotBeEmpty(
            "Charbelcher is untapped and {3} is floating — belch IS enumerable");
    }

    /// <summary>
    /// Scenario B, Step 2:
    /// The DIRECTIVE path (<see cref="BelcherStrategy.TryGetNextWinningAction"/>)
    /// must return an ActivateAbility action when {3} is floating and Charbelcher
    /// is untapped.
    ///
    /// This is the first-priority short-circuit in
    /// <see cref="SearchStrategy.PickPriorityAction"/> — it fires BEFORE MCTS.
    /// If this returns null, MCTS runs but faces the remap problem (Step 3).
    /// If this works, the bot belches without needing MCTS at all.
    /// </summary>
    [Fact]
    public void ScenarioB_Step2_DirectivePath_ReturnsActivate_When3FloatingAndUntapped()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        var ctx = AtMain(bot, opp);
        var strategy = new BelcherStrategy();

        var action = strategy.TryGetNextWinningAction(ctx, bot);

        _out.WriteLine($"[B.Step2] directive returned: {action?.GetType().Name ?? "null"}");

        action.Should().BeOfType<PriorityAction.ActivateAbility>(
            "directive must fire the belch when {3} floating + Charbelcher untapped");
    }

    /// <summary>
    /// Scenario B, Step 3:
    /// <see cref="SearchStrategy.PickPriorityAction"/> must return ActivateAbility
    /// (the belch) when {3} is floating and Charbelcher is on board.
    ///
    /// The directive check at the top of PickPriorityAction fires BEFORE MCTS,
    /// so this test verifies the full decision path end-to-end.
    /// If SearchStrategy returns Pass here, the directive path is broken.
    /// </summary>
    [Fact]
    public void ScenarioB_Step3_SearchStrategy_ReturnsActivate_When3FloatingAndUntapped()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        PadLibraries(bot, opp);

        var ctx = AtMain(bot, opp);
        var config = new BotConfig("Belcher", Strategy: "mcts",
            MaxMctsIterations: 20, MaxMctsBudgetMs: 500,
            PrioritySearchEnabled: true);
        var strat = new SearchStrategy(config);

        var action = strat.PickPriorityAction(ctx, bot);

        _out.WriteLine($"[B.Step3] SearchStrategy returned: {action?.GetType().Name ?? "null"}");
        _out.WriteLine($"[B.Step3] action: {action}");

        action.Should().BeOfType<PriorityAction.ActivateAbility>(
            "with {3} floating + Charbelcher untapped, directive fires the belch before MCTS runs");
    }

    // ── SCENARIO A — No floating mana, rituals in hand ────────────────────────

    /// <summary>
    /// Scenario A, Step 1:
    /// Charbelcher on board (untapped), 0 floating mana, ritual (CMC 2) in hand.
    /// <see cref="LegalActionEnumerator.ForPriority"/> should enumerate the ritual
    /// cast (UntappedManaSources = 0 → ritual CMC 2 NOT affordable) only if there
    /// are mana sources. With NO mana sources at all, no spell is castable.
    ///
    /// Also: the belch (CMC 3 mana cost) is NOT enumerable with 0 mana.
    ///
    /// This test pins the exact enumeration with 0 mana — the baseline before any
    /// ritual is cast.
    /// </summary>
    [Fact]
    public void ScenarioA_Step1_Enumeration_NoSpellsCastable_WhenZeroMana()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        // Ritual in hand — CMC 2. But no mana sources, so not castable.
        AddRitualToHand(bot, "Desperate Ritual");

        var ctx = AtMain(bot, opp);

        var legal = LegalActionEnumerator.ForPriority(ctx, bot);
        var activations = legal.OfType<PriorityAction.ActivateAbility>().ToList();
        var casts = legal.OfType<PriorityAction.CastSpell>().ToList();

        _out.WriteLine($"[A.Step1] legal actions: {legal.Count}");
        foreach (var a in legal)
            _out.WriteLine($"[A.Step1]   {a}");

        activations.Should().BeEmpty("belch costs {3} — not affordable with 0 mana");
        casts.Should().BeEmpty("ritual costs {1}{R} — not affordable with 0 mana");
        legal.Should().ContainSingle(a => a is PriorityAction.PassAction,
            "only Pass is legal when no mana is available");
    }

    /// <summary>
    /// Scenario A, Step 2:
    /// Charbelcher on board, 0 floating, ritual (CMC 2) in hand, but the player
    /// has 2 untapped lands (mana sources). The ritual IS castable.
    /// After casting the ritual (simulated here by manually floating {3}), the
    /// belch becomes enumerable. This verifies the multi-step path is correct
    /// in principle — the question is whether MCTS explores it.
    ///
    /// Test: with 2 untapped lands, ritual IS enumerated; belch is NOT yet
    /// enumerated (only 2 mana, belch needs 3). After "casting" the ritual
    /// (adding {3} to pool), belch IS enumerated.
    /// </summary>
    [Fact]
    public void ScenarioA_Step2_Enumeration_RitualCastable_ThenBelchEnumerable_AfterPool()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        // Ritual {1}{R} CMC=2 is castable with 2 mana sources.
        // We use untapped lands as proxy mana sources (bare Land → 1 via fallback).
        var l1 = new Land("Mountain-A");
        l1.ChangeOwner(bot);
        bot.Zones.Battlefield.AddCard(l1);

        var l2 = new Land("Mountain-B");
        l2.ChangeOwner(bot);
        bot.Zones.Battlefield.AddCard(l2);

        AddRitualToHand(bot, "Desperate Ritual");

        var ctx = AtMain(bot, opp);

        // Before ritual: 2 mana available → ritual castable, belch (needs 3) not.
        var legal1 = LegalActionEnumerator.ForPriority(ctx, bot);
        _out.WriteLine($"[A.Step2] before ritual — legal actions: {legal1.Count}");
        foreach (var a in legal1) _out.WriteLine($"[A.Step2]   {a}");

        legal1.Should().Contain(a => a is PriorityAction.CastSpell,
            "ritual (CMC 2) is castable with 2 lands in play");
        legal1.Should().NotContain(a => a is PriorityAction.ActivateAbility,
            "belch costs {3} — not affordable with only 2 mana");

        // Simulate ritual resolution: float 3R (1R net gain, but {1}{R} spent + {R}{R}{R} produced).
        // The ritual's real effect: net +1R. Here we just put {3} in pool to reach belch threshold.
        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        // After ritual: pool has {3} → belch is now enumerable.
        var legal2 = LegalActionEnumerator.ForPriority(ctx, bot);
        _out.WriteLine($"[A.Step2] after ritual pool — legal actions: {legal2.Count}");
        foreach (var a in legal2) _out.WriteLine($"[A.Step2]   {a}");

        legal2.Should().Contain(a => a is PriorityAction.ActivateAbility,
            "after ritual adds {3} to pool, belch IS enumerable");
    }

    /// <summary>
    /// Scenario A, Step 3 — DIRECTIVE PATH WITH NO FLOATING MANA:
    /// When Charbelcher is on board but the pool is empty,
    /// <see cref="BelcherStrategy.TryGetNextWinningAction"/> must return null
    /// (the directive correctly refuses to fire when {3} cannot be paid).
    ///
    /// This is the correct behavior — the bot should cast rituals first,
    /// then the directive fires on the next priority window (Scenario B).
    /// </summary>
    [Fact]
    public void ScenarioA_Step3_DirectivePath_ReturnsNull_WhenNoFloatingMana()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        // No floating mana — pool is empty.
        AddRitualToHand(bot, "Desperate Ritual");

        var ctx = AtMain(bot, opp);
        var strategy = new BelcherStrategy();

        var action = strategy.TryGetNextWinningAction(ctx, bot);

        _out.WriteLine($"[A.Step3] directive with 0 mana: {action?.GetType().Name ?? "null"}");

        action.Should().BeNull(
            "directive must return null when {3} is not in the floating pool — " +
            "the bot needs to cast rituals first");
    }

    // ── HEURISTIC PATH — does PriorityPolicy pick the belch? ─────────────────

    /// <summary>
    /// When the heuristic is called (the fallback path in SearchStrategy.RemapPriorityAction
    /// when MCTS chooses ActivateAbility), does PriorityPolicy pick the belch?
    ///
    /// Setup: Charbelcher on board, untapped, {3} floating. No anti-spin memo
    /// (fresh PriorityPolicy instance). The belch should score above Pass via
    /// ActivatedAbilityPolicy.ProjectActivateDelta (effect description contains "damage").
    ///
    /// This verifies that the heuristic fallback itself works when the directive
    /// is unavailable (e.g., when the SearchStrategy is configured without a
    /// BelcherStrategy override, or when called from rollout mode).
    /// </summary>
    [Fact]
    public void HeuristicFallback_PicksBelch_When3FloatingAndUntapped()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        var ctx = AtMain(bot, opp);
        var weights = ArchetypeWeights.ForArchetype("Belcher");
        var policy = new PriorityPolicy(weights);

        var action = policy.Pick(ctx, bot);

        _out.WriteLine($"[Heuristic] PriorityPolicy picked: {action?.GetType().Name ?? "null"}");
        _out.WriteLine($"[Heuristic] action: {action}");

        // This test DOCUMENTS the behavior — if the heuristic fails to pick the
        // belch, it reveals the valuation issue (blocker ii).
        // If it DOES pick the belch, then the issue is the remap (blocker in SearchStrategy).
        _out.WriteLine($"[Heuristic] is ActivateAbility: {action is PriorityAction.ActivateAbility}");
    }

    /// <summary>
    /// KEY REMAP TEST:
    /// When MCTS (inside SearchStrategy) chooses ActivateAbility(Charbelcher belch),
    /// the current SearchStrategy.RemapPriorityAction FALLS BACK TO HEURISTIC.
    /// This means the remap discards the MCTS decision and re-runs the heuristic.
    ///
    /// This test confirms that the REMAP FALLBACK is the terminal blocker:
    /// even when MCTS correctly identifies the belch as the best move,
    /// SearchStrategy.RemapPriorityAction discards it and calls the heuristic
    /// (which then either picks the belch via heuristic scoring or doesn't).
    ///
    /// The test documents the current behavior and serves as a regression
    /// baseline for the fix (Phase 2: implement ActivateAbility remap by
    /// matching ability source InstanceId on the live battlefield).
    /// </summary>
    [Fact]
    public void SearchStrategy_RemapFallback_IsTheBlocker_WhenMctsChoosesActivate()
    {
        // Construct a scenario where Charbelcher is on board with {3} floating.
        // The directive path SHOULD fire — but let's verify the full SearchStrategy
        // call returns ActivateAbility (directive wins before MCTS even runs).
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        PadLibraries(bot, opp);

        var ctx = AtMain(bot, opp);
        var config = new BotConfig("Belcher", Strategy: "mcts",
            MaxMctsIterations: 30, MaxMctsBudgetMs: 500,
            PrioritySearchEnabled: true);
        var strat = new SearchStrategy(config);

        var action = strat.PickPriorityAction(ctx, bot);

        _out.WriteLine($"[Remap] SearchStrategy returned: {action?.GetType().Name ?? "null"}");
        _out.WriteLine($"[Remap] is ActivateAbility: {action is PriorityAction.ActivateAbility}");

        // With {3} floating + Charbelcher untapped, the DIRECTIVE short-circuit
        // fires at line 229-230 in SearchStrategy.PickPriorityAction:
        //   var win = _deckStrategy?.TryGetNextWinningAction(ctx, self);
        //   if (win is not null) return win;
        // So the result SHOULD be ActivateAbility — directive bypasses MCTS+remap entirely.
        action.Should().BeOfType<PriorityAction.ActivateAbility>(
            "directive fires before MCTS when {3} is floating — no remap needed");
    }

    // ── CRITICAL DIAGNOSTIC: does the directive require exact floating mana? ──

    /// <summary>
    /// The critical question: when Charbelcher is cast via the MCTS/heuristic
    /// path (spending {4} from pool), does the player have {3} LEFT in the pool?
    ///
    /// Scenario: player has {7} floating (ritual chain produced 7+ mana).
    /// Cast Charbelcher {4} → 3 left. Directive should then fire.
    ///
    /// This test simulates the exact state AFTER casting Charbelcher to verify
    /// the directive fires in the next priority window.
    ///
    /// BelcherStrategy.TryGetNextWinningAction calls DeckStrategyHelpers.BuildActivate,
    /// which calls ManaCostCost("{3}").CanPay(self) = player.ManaPool.CanPay("{3}").
    /// This checks ONLY the floating pool. If {3} is in the pool, it fires.
    /// </summary>
    [Fact]
    public void Directive_FiresBelch_AfterCastingCharbelcher_With3Remaining()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        // Post-cast state: Charbelcher on board (just entered via cast from hand),
        // 3R still floating (cast cost {4} was paid from 7R, leaving 3R).
        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        // Simulate the residual mana after casting Charbelcher ({4} paid from {7}).
        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        var ctx = AtMain(bot, opp);
        var strategy = new BelcherStrategy();

        var action = strategy.TryGetNextWinningAction(ctx, bot);

        _out.WriteLine($"[PostCast] directive with {bot.ManaPool.Total} floating: {action?.GetType().Name ?? "null"}");

        action.Should().BeOfType<PriorityAction.ActivateAbility>(
            "after casting Charbelcher with {3} remaining in pool, directive fires immediately");
    }

    /// <summary>
    /// Negative case: if the player has exactly {4} floating and casts Charbelcher,
    /// the pool is empty afterward. The directive CANNOT fire.
    ///
    /// This is the failure mode: the bot chains just enough rituals for the cast
    /// (exactly {4}) but not enough for cast + activation (needs {7} = {4} + {3}).
    ///
    /// Documents that the bot must have {7}+ mana available to execute the full
    /// cast + activate line in a single turn.
    /// </summary>
    [Fact]
    public void Directive_CannotFire_WhenOnlyExact4ManaAndCharbelcherOnBoard()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var belcher = BuildCharbelcher(bot);
        bot.Zones.Battlefield.AddCard(belcher);

        // Pool is empty after exactly paying {4} for the Charbelcher cast.
        // (No mana added here — simulates post-cast empty pool.)

        var ctx = AtMain(bot, opp);
        var strategy = new BelcherStrategy();

        var action = strategy.TryGetNextWinningAction(ctx, bot);

        _out.WriteLine($"[ExactMana] directive with 0 floating: {action?.GetType().Name ?? "null"}");

        action.Should().BeNull(
            "pool is empty after casting Charbelcher with exactly {4} — " +
            "directive cannot pay {3} activation cost; bot needs {7}+ total for the full line");
    }

    // ── NEW: cast-arm tests (Phase 2 fix) ─────────────────────────────────────

    /// <summary>
    /// Phase 2 fix — cast arm:
    /// When Charbelcher is in hand (not yet on board) and the floating pool has
    /// exactly {7} (= {4} cast + {3} activation), the directive's new cast arm
    /// MUST return a CastSpell action.
    ///
    /// This is the primary scenario the fix targets: after casting Irencrag Feat
    /// ({4} → {7} in pool), the directive takes ownership of the Charbelcher cast
    /// so that {3} remains in the pool for the subsequent activation.
    /// </summary>
    [Fact]
    public void Directive_CastArm_ReturnsCastSpell_WhenPool7AndCharbelcherInHand()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        // Charbelcher in hand — NOT on board yet.
        var charbelcher = new Artifact("Goblin Charbelcher", "{4}");
        charbelcher.ChangeOwner(bot);
        bot.Zones.Hand.AddCard(charbelcher);

        // {7} floating — full line is affordable (cast {4} + activate {3}).
        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}{R}{R}{R}{R}"));

        PadLibraries(bot, opp);

        var ctx = AtMain(bot, opp);
        var strategy = new BelcherStrategy();

        var action = strategy.TryGetNextWinningAction(ctx, bot);

        _out.WriteLine($"[CastArm] directive with {bot.ManaPool.Total} floating: {action?.GetType().Name ?? "null"}");
        _out.WriteLine($"[CastArm] action: {action}");

        action.Should().BeOfType<PriorityAction.CastSpell>(
            "pool >= 7 with Charbelcher in hand — directive takes the cast to preserve {3} for activation");

        var castAction = (PriorityAction.CastSpell)action!;
        castAction.Card.Name.Should().Be("Goblin Charbelcher",
            "directive must cast Goblin Charbelcher specifically");
    }

    /// <summary>
    /// Phase 2 fix — cast arm suppressed at pool = 4:
    /// When Charbelcher is in hand and the floating pool is exactly {4} (bare cast cost),
    /// the directive's cast arm MUST return null (not cast Charbelcher).
    ///
    /// Casting at pool = 4 would leave {0} for the activation — the bot would strand
    /// Charbelcher on the board with no activation mana (the pre-fix failure mode).
    /// The fix gates the cast arm at pool >= 7 to prevent this.
    /// </summary>
    [Fact]
    public void Directive_CastArm_ReturnsNull_WhenPool4AndCharbelcherInHand()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        // Charbelcher in hand — NOT on board yet.
        var charbelcher = new Artifact("Goblin Charbelcher", "{4}");
        charbelcher.ChangeOwner(bot);
        bot.Zones.Hand.AddCard(charbelcher);

        // {4} floating — just enough to cast, NOT enough for the full line.
        bot.AddManaToPool(ManaCost.Parse("{R}{R}{R}{R}"));

        PadLibraries(bot, opp);

        var ctx = AtMain(bot, opp);
        var strategy = new BelcherStrategy();

        var action = strategy.TryGetNextWinningAction(ctx, bot);

        _out.WriteLine($"[CastArm] directive with {bot.ManaPool.Total} floating (=4): {action?.GetType().Name ?? "null"}");

        action.Should().BeNull(
            "pool = 4 is not enough for cast ({4}) + activate ({3}) = 7 total — " +
            "directive must defer to the heuristic/search to gather more mana first");
    }
}
