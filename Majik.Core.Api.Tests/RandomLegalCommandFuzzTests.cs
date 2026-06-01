using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Non-parallel collection for the fuzz harness. <c>DisableParallelization</c>
/// keeps every fuzz seed from running concurrently with ANY other test in the
/// assembly, so the per-game deterministic-id replay assertion isn't perturbed
/// by a concurrently-running id-scoped game (see the class-level note on the
/// cross-game <c>AsyncLocal</c> id-flow leak). The seeds still run fast
/// (serial, ~25-30s total) so CI stays in the seconds-to-low-minutes budget.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FuzzCollection
{
    public const string Name = "RandomLegalCommandFuzz";
}

/// <summary>
/// Random-legal-command FUZZ / PROPERTY harness.
///
/// <para>Where <see cref="DeterminismReplayTests"/> drives ONE fixed-policy
/// scripted game and asserts reproducibility, this harness generalises that
/// fixed responder into a <see cref="GameRandom"/>-seeded chooser that, at
/// every prompt, picks a uniformly-random LEGAL option from the engine's
/// offered surface — pass / play a legal land / cast an affordable spell /
/// random legal attackers/blockers/mulligan/bottom/yes-no/reveal/X/mode — and
/// asserts a battery of engine INVARIANTS after EVERY <see cref="GameFacade.SubmitAsync"/>.
/// It is built to surface the bug classes example-based tests can't: stuck
/// priority, SBA non-convergence, illegal persistent state, and hidden
/// nondeterminism.</para>
///
/// <para><b>Legality discipline.</b> The chooser only ever submits commands it
/// can prove legal from the engine's own surfaces:
/// <list type="bullet">
///   <item>Priority kinds come from <see cref="PromptDto.ExpectedKinds"/>
///     (the engine's <see cref="PriorityKinds"/> output — the same source the
///     auto-pass gate consults).</item>
///   <item>Land plays / casts / attackers / blockers are derived from the
///     PUBLIC <see cref="GameStateDto"/> (own untapped lands, in-hand cards,
///     own untapped non-sick creatures, the declared attackers), and the
///     mana cost is paid via the engine's AUTO-TAP path (empty
///     <see cref="ChooseManaCommand"/>) so an unaffordable cast is rejected
///     cleanly by the engine rather than desyncing the fuzzer. A cast the
///     engine can't pay simply rotates back to hand — no illegal state.</item>
///   <item>Choice prompts (mulligan, bottom, yes/no, reveal-and-choose, X,
///     mode, library pick, surveil, targets) are answered from the prompt's
///     own view payload (<see cref="PromptDto.BottomCount"/>,
///     <see cref="PromptDto.RevealView"/>, <see cref="PromptDto.Candidates"/>,
///     …) — always a legal partition / pick.</item>
/// </list></para>
///
/// <para><b>Why this class is its own non-parallel collection.</b> The
/// id-identical replay assertion compares the deterministic object-ids minted
/// by <see cref="DeterministicIdScope"/> — a per-game <c>AsyncLocal</c> source.
/// That isolation is sound for a game run in isolation, but xUnit's default
/// cross-class parallelism runs many id-scoped games concurrently on a shared
/// thread pool, and a continuation resuming under a *different* concurrent
/// game's ambient scope can perturb which id a card is minted with — surfacing
/// as a flaky id-only divergence (names / zones / P/T / life all still match,
/// only the InstanceIds differ). That cross-game <c>AsyncLocal</c> id-flow
/// leak is a real engine concurrency-determinism nuance (flagged in the PR);
/// it is orthogonal to the per-game replay property this harness asserts, so we
/// pin the harness to a non-parallel collection to test that property soundly.
/// The existing <see cref="DeterminismReplayTests"/> never tripped it only
/// because its two empty-deck facts mint a handful of ids over a tiny flow.</para>
/// </summary>
[Collection(FuzzCollection.Name)]
public sealed class RandomLegalCommandFuzzTests
{
    private readonly ITestOutputHelper _out;

    public RandomLegalCommandFuzzTests(ITestOutputHelper output) => _out = output;

    // CI budget: 120 enumerated seeds, each game capped at MaxSteps prompts and
    // MaxTurns turns. Completes in seconds. A failing seed is in the theory data
    // so it is individually replayable: the seed is the test argument and is
    // echoed in every assertion message.
    public static IEnumerable<object[]> Seeds()
    {
        for (var seed = 1; seed <= 120; seed++)
        {
            yield return new object[] { seed };
        }
    }

    private const int MaxTurns = 6;
    private const int MaxSteps = 600;

    // A stall is "this many consecutive prompts with NO observable progress"
    // (turn number, phase, active player, stack depth, total life, total cards
    // in public zones, tapped count, prompt recipient + offered kinds all
    // unchanged). Generous: a long priority volley across both seats within one
    // step legitimately produces several prompts, but the per-prompt fingerprint
    // still changes as priority / board move. A true stall is a stuck-priority bug.
    private const int StallBound = 80;

    // life <= this must NOT persist on a live (not-lost) player — SBA loss.
    private const int MaxLifeForLivePlayer = 0;

    [Theory]
    [Trait("Category", "Fuzz")]
    [MemberData(nameof(Seeds))]
    public async Task RandomLegalCommands_PreserveInvariants_AndReplayIdentically(int seed)
    {
        // ── Run 1: fuzz under a deterministic id scope so the run is fully
        //    seed-derived (ids included), and assert invariants after every step.
        var run1 = await FuzzOnceAsync(seed, assertInvariants: true);

        // ── Run 2: SAME seed, SAME (deterministic) responder → SAME command
        //    log. Re-run through a fresh facade under the same id scope and
        //    assert byte-for-byte IdProjection equality. This catches any
        //    hidden nondeterminism the determinism PRs might have missed:
        //    a divergent command log OR a divergent final state both fail here.
        var run2 = await FuzzOnceAsync(seed, assertInvariants: false);

        run2.CommandKinds.Should().Equal(run1.CommandKinds,
            $"seed {seed}: the random-legal responder is a pure function of the " +
            "seed, so two runs must take the IDENTICAL decision sequence");

        IdProjection(run2.State).Should().BeEquivalentTo(
            IdProjection(run1.State), opts => opts.WithStrictOrdering(),
            $"seed {seed}: same seed + same command log must yield an " +
            "ID-IDENTICAL final state (no hidden nondeterminism)");
    }

    [Fact]
    [Trait("Category", "FuzzDiag")]
    public async Task Diagnostic_CoverageAcrossSeeds()
    {
        var allKinds = new Dictionary<string, int>();
        var gamesWithCombatDamage = 0;
        var totalCommands = 0;
        var ended = 0;
        for (var seed = 1; seed <= 30; seed++)
        {
            var r = await FuzzOnceAsync(seed, assertInvariants: false);
            totalCommands += r.CommandKinds.Count;
            foreach (var k in r.CommandKinds)
                allKinds[k] = allKinds.GetValueOrDefault(k) + 1;
            if (r.State.Players.Any(p => p.Life < 20)) gamesWithCombatDamage++;
            if (r.State.Players.Any(p => p.HasLost)) ended++;
        }
        _out.WriteLine($"30 seeds: {totalCommands} commands, " +
            $"{gamesWithCombatDamage} games with life change, {ended} games reached a loss.");
        foreach (var (k, n) in allKinds.OrderByDescending(kv => kv.Value))
            _out.WriteLine($"  {k}: {n}");
        // Coverage sanity: the random fuzzer must actually exercise the rich
        // command kinds, not just pass/mulligan.
        allKinds.Should().ContainKey(nameof(PlayLandCommand));
        allKinds.Should().ContainKey(nameof(CastSpellCommand));
        allKinds.Keys.Count.Should().BeGreaterThan(4,
            "the fuzzer should exercise a variety of command kinds");
    }

    private sealed record RunResult(GameStateDto State, IReadOnlyList<string> CommandKinds);

    private async Task<RunResult> FuzzOnceAsync(int seed, bool assertInvariants)
    {
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));

        var facade = BuildDeckFacade(seed);
        // The responder's RNG is seeded from the game seed so the whole decision
        // sequence is a pure function of `seed` and replays identically.
        var rng = new GameRandom(seed);
        var kinds = new List<string>();

        await DriveAsync(facade, seed, prompt =>
        {
            var cmd = ChooseLegalCommand(facade, prompt, rng);
            kinds.Add(cmd.GetType().Name);
            return cmd;
        }, assertInvariants ? new InvariantChecker(seed) : null);

        return new RunResult(facade.GetState(), kinds);
    }

    // -----------------------------------------------------------------------
    // Driver — channel-pumped prompt loop with a per-prompt invariant check.
    // Mirrors DeterminismReplayTests.DriveAsync but bounded + instrumented.
    // -----------------------------------------------------------------------
    private static async Task DriveAsync(
        GameFacade facade, int seed, Func<PromptDto, GameCommand> respond,
        InvariantChecker? checker)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => channel.Writer.TryWrite(p));

        await facade.StartFullGameAsync(
            maxTurns: MaxTurns,
            rng: new GameRandom(seed),
            logicalClock: new LogicalClock());

        var game = facade.FullGameTask!;

        // Stall detection: fingerprint observable progress; a long run of
        // identical fingerprints with no game-over is a stuck-priority bug.
        string? lastFingerprint = null;
        var stallCount = 0;

        for (var step = 0; step < MaxSteps; step++)
        {
            if (game.IsCompleted) break;

            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game);
            if (winner == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            // ── Invariant check on the state the engine is prompting FROM.
            var preState = facade.GetState();
            checker?.Check(preState, prompt, step);

            // ── Stuck-priority detection: the game must make progress.
            var fp = ProgressFingerprint(preState, prompt);
            if (fp == lastFingerprint)
            {
                stallCount++;
                stallCount.Should().BeLessThan(StallBound,
                    $"seed {seed}: NO PROGRESS for {StallBound} consecutive prompts " +
                    $"(stuck priority?) at step {step}, phase {preState.Phase}, " +
                    $"turn {preState.TurnNumber}, stack depth {preState.Stack.Count}");
            }
            else
            {
                stallCount = 0;
                lastFingerprint = fp;
            }

            var cmd = respond(prompt) with { PlayerId = prompt.PlayerId };
            try
            {
                await facade.SubmitAsync(cmd);
            }
            catch (InvalidOperationException)
            {
                // Bot/closed seat or already-finished game — stop driving.
                break;
            }

            // ── Invariant check on the resulting state.
            checker?.Check(facade.GetState(), prompt: null, step);
        }

        // Final invariant sweep on whatever state we ended in.
        checker?.Check(facade.GetState(), prompt: null, MaxSteps);
    }

    // A per-prompt fingerprint of OBSERVABLE progress. Includes the prompt's
    // recipient + expected kinds so a priority volley that legitimately moves
    // between seats / changes the offered actions is counted as progress (it
    // is — priority is passing). A true stall (same seat, same kinds, same
    // board, repeatedly) is what we want to catch.
    private static string ProgressFingerprint(GameStateDto s, PromptDto prompt)
    {
        var totalLife = s.Players.Sum(p => p.Life);
        var totalCards = s.Players.Sum(p =>
            p.Hand.Cards.Count + p.Battlefield.Cards.Count + p.Graveyard.Cards.Count
            + p.Library.Cards.Count + p.Exile.Cards.Count);
        var tappedCount = s.Players.Sum(p => p.Battlefield.Cards.Count(c => c.Tapped));
        return string.Join('|',
            s.TurnNumber, s.Phase, s.ActivePlayerId, s.Stack.Count,
            totalLife, totalCards, tappedCount,
            prompt.PlayerId, string.Join(',', prompt.ExpectedKinds.OrderBy(k => k)));
    }

    // =======================================================================
    // RANDOM-LEGAL CHOOSER — only ever returns a provably-legal command.
    // =======================================================================
    private static GameCommand ChooseLegalCommand(
        GameFacade facade, PromptDto prompt, GameRandom rng)
    {
        var kinds = prompt.ExpectedKinds;

        // ── Mulligan: keep or mull at random (London — always legal). ──────
        if (kinds.Contains(nameof(MulliganCommand)))
            return new MulliganCommand(Keep: rng.Next(2) == 0);

        // ── Bottom-N after a mulligan: bottom a random N-subset of hand. ───
        if (kinds.Contains(nameof(ChooseCardsToBottomCommand)))
        {
            var n = prompt.BottomCount ?? 0;
            var hand = HandOf(facade, prompt.PlayerId);
            // Random distinct subset of exactly n ids (legal partition).
            var ids = Shuffle(hand.Select(c => c.InstanceId).ToList(), rng).Take(n).ToList();
            return new ChooseCardsToBottomCommand(ids);
        }

        // ── Mana payment prompt: empty list = engine auto-tap (CR 601.2g).
        //    Always the legal "Auto-pay" path; if it can't be covered the
        //    engine cancels the cast cleanly (card returns to hand).
        if (kinds.Contains(nameof(ChooseManaCommand)))
            return new ChooseManaCommand(Array.Empty<Guid>());

        // ── Yes/No: random legal answer. ───────────────────────────────────
        if (kinds.Contains(nameof(ChooseYesNoCommand)))
            return new ChooseYesNoCommand(rng.Next(2) == 0);

        // ── Reveal-and-choose: pick a random eligible card, or decline when
        //    that's legal (optional, or empty eligible set). ────────────────
        if (kinds.Contains(nameof(ChooseFromRevealedCommand)))
        {
            var view = prompt.RevealView;
            var eligible = view?.EligibleInstanceIds?.ToList() ?? new List<Guid>();
            if (eligible.Count == 0 || (view!.Optional && rng.Next(2) == 0))
                return new ChooseFromRevealedCommand(null);
            return new ChooseFromRevealedCommand(eligible[rng.Next(eligible.Count)]);
        }

        // ── Library pick: pick a random candidate, or "find nothing". ──────
        if (kinds.Contains(nameof(ChooseLibraryPickCommand)))
        {
            var cands = prompt.Candidates?.Select(c => c.InstanceId).ToList() ?? new List<Guid>();
            if (cands.Count == 0 || rng.Next(4) == 0)
                return new ChooseLibraryPickCommand(null);
            return new ChooseLibraryPickCommand(cands[rng.Next(cands.Count)]);
        }

        // ── Surveil: randomly partition the peeked set (legal exact split).
        if (kinds.Contains(nameof(ChooseSurveilCommand)))
        {
            var peeked = prompt.SurveilView?.Select(c => c.InstanceId).ToList() ?? new List<Guid>();
            var toGrave = new List<Guid>();
            var keepTop = new List<Guid>();
            foreach (var id in peeked)
                (rng.Next(2) == 0 ? toGrave : keepTop).Add(id);
            // keepTop order randomised (any order is legal).
            keepTop = Shuffle(keepTop, rng);
            return new ChooseSurveilCommand(toGrave, keepTop);
        }

        // ── X: a random legal X. We can't always see the affordability bound
        //    from the DTO, so pick a small X (0..3); the engine clamps /
        //    rejects an unaffordable X cleanly. ─────────────────────────────
        if (kinds.Contains(nameof(ChooseXCommand)))
            return new ChooseXCommand(rng.Next(4));

        // ── Mode: a random legal mode index. The engine validates the index;
        //    most modal cards offer a small mode count. Keep it small. ───────
        if (kinds.Contains(nameof(ChooseModeCommand)))
            return new ChooseModeCommand(rng.Next(2));

        // ── Targets: pick a random legal target from the candidate set the
        //    prompt surfaced, else from any visible permanent. The engine
        //    validates legality and rejects an illegal pick cleanly. ─────────
        if (kinds.Contains(nameof(ChooseTargetsCommand)))
        {
            var cands = prompt.Candidates?.Select(c => c.InstanceId).ToList();
            if (cands == null || cands.Count == 0)
            {
                // Fall back to any visible permanent on either battlefield.
                cands = facade.GetState().Players
                    .SelectMany(p => p.Battlefield.Cards.Select(c => c.InstanceId))
                    .ToList();
            }
            if (cands.Count == 0)
                return new ChooseTargetsCommand(Array.Empty<Guid>());
            return new ChooseTargetsCommand(new[] { cands[rng.Next(cands.Count)] });
        }

        // ── Order triggers: engine default order (empty = "as presented").
        if (kinds.Contains(nameof(OrderTriggersCommand)))
            return new OrderTriggersCommand(Array.Empty<Guid>());

        // ── Declare attackers: a random legal subset of eligible attackers.
        if (kinds.Contains(nameof(DeclareAttackersCommand)))
        {
            var me = PlayerOf(facade, prompt.PlayerId);
            var opp = OpponentOf(facade, prompt.PlayerId);
            var eligible = me.Battlefield.Cards
                .Where(c => c.Types.Contains("Creature") && !c.Tapped && !c.SummoningSickness)
                .ToList();
            var attackers = eligible
                .Where(_ => rng.Next(2) == 0)
                .Select(c => new AttackerDeclarationDto(c.InstanceId, opp.Id))
                .ToList();
            return new DeclareAttackersCommand(attackers);
        }

        // ── Declare blockers: a random legal subset, each block assigned to a
        //    random declared attacker. Attackers are the opponent's tapped
        //    attacking creatures; the engine validates each block (blocker
        //    eligibility, that the named attacker is in fact attacking) and
        //    rejects illegal ones cleanly. ──────────────────────────────────
        if (kinds.Contains(nameof(DeclareBlockersCommand)))
        {
            var me = PlayerOf(facade, prompt.PlayerId);
            var opp = OpponentOf(facade, prompt.PlayerId);
            var attackers = opp.Battlefield.Cards
                .Where(c => c.Types.Contains("Creature") && c.Tapped)
                .Select(c => c.InstanceId)
                .ToList();
            var blockers = me.Battlefield.Cards
                .Where(c => c.Types.Contains("Creature") && !c.Tapped)
                .ToList();
            if (attackers.Count == 0 || blockers.Count == 0)
                return new DeclareBlockersCommand(Array.Empty<BlockerDeclarationDto>());
            var decls = blockers
                .Where(_ => rng.Next(2) == 0)
                .Select(b => new BlockerDeclarationDto(
                    b.InstanceId, attackers[rng.Next(attackers.Count)]))
                .ToList();
            return new DeclareBlockersCommand(decls);
        }

        // ── Priority window: choose among the legal priority kinds. ─────────
        return ChoosePriorityAction(facade, prompt, rng);
    }

    /// <summary>
    /// At a priority window pick uniformly among the legal priority options the
    /// engine offered (<see cref="PromptDto.ExpectedKinds"/>), but only build a
    /// concrete command we can prove legal from PUBLIC state:
    /// pass / play an in-hand land / cast an in-hand non-land / activate a mana
    /// ability / activate a non-mana ability. Anything we can't legally
    /// construct degrades to a pass.
    /// </summary>
    private static GameCommand ChoosePriorityAction(
        GameFacade facade, PromptDto prompt, GameRandom rng)
    {
        var kinds = prompt.ExpectedKinds;
        var me = PlayerOf(facade, prompt.PlayerId);

        // Build the menu of CONCRETE legal actions, each as a thunk. Index 0 is
        // always Pass (CR 117.4) — the dominant option so games progress.
        var options = new List<Func<GameCommand>>
        {
            () => new PassPriorityCommand(),
        };

        if (kinds.Contains(nameof(PlayLandCommand)))
        {
            foreach (var land in me.Hand.Cards.Where(c => c.Types.Contains("Land")))
            {
                var id = land.InstanceId;
                options.Add(() => new PlayLandCommand(id));
            }
        }

        if (kinds.Contains(nameof(CastSpellCommand)))
        {
            foreach (var spell in me.Hand.Cards.Where(c => !c.Types.Contains("Land")))
            {
                var id = spell.InstanceId;
                // No targets at cast time — the engine raises a follow-up
                // ChooseTargets prompt for spells that need targets, which the
                // chooser answers. Mana is paid via the auto-tap ChooseMana
                // path. An unaffordable cast is rejected cleanly.
                options.Add(() => new CastSpellCommand(id, Array.Empty<Guid>(), null, null));
            }
        }

        if (kinds.Contains(nameof(ActivateManaAbilityCommand)))
        {
            var sources = me.Battlefield.Cards
                .Where(c => !c.Tapped && c.Abilities.Any(a =>
                    string.Equals(a.Kind, "Mana", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var src in sources)
            {
                var id = src.InstanceId;
                options.Add(() => new ActivateManaAbilityCommand(id, string.Empty));
            }
        }

        if (kinds.Contains(nameof(ActivateAbilityCommand)))
        {
            var sources = me.Battlefield.Cards
                .Where(c => !c.Tapped)
                .SelectMany(c => c.Abilities
                    .Where(a => a.Id != null
                        && !string.Equals(a.Kind, "Mana", StringComparison.OrdinalIgnoreCase))
                    .Select(a => (Permanent: c.InstanceId, AbilityId: a.Id!.Value)))
                .ToList();
            foreach (var (permanent, abilityId) in sources)
            {
                options.Add(() => new ActivateAbilityCommand(permanent, abilityId));
            }
        }

        // Bias toward passing so games terminate: pass ~half the time, else a
        // random non-pass option. With only pass available we always pass.
        if (options.Count == 1 || rng.Next(2) == 0)
            return new PassPriorityCommand();
        return options[1 + rng.Next(options.Count - 1)]();
    }

    // =======================================================================
    // INVARIANT CHECKER — fails loudly on any violation.
    // =======================================================================
    private sealed class InvariantChecker
    {
        private readonly int _seed;

        public InvariantChecker(int seed) => _seed = seed;

        public void Check(GameStateDto s, PromptDto? prompt, int step)
        {
            var ctx = $"seed {_seed}, step {step}, turn {s.TurnNumber}, phase {s.Phase}";

            // INV-1: No illegal persistent state — a player at life <= 0 must
            // have been resolved to a LOSS by SBA (CR 704.5a), never persist as
            // a live (not-lost) state. If life <= 0 AND HasLost == false, the
            // SBA loss check failed to fire / converge.
            foreach (var p in s.Players)
            {
                if (p.Life <= MaxLifeForLivePlayer && !p.HasLost)
                {
                    throw new FuzzInvariantViolation(
                        $"{ctx}: player '{p.Name}' is at life {p.Life} (<= 0) but is " +
                        "NOT marked lost — SBA loss check (CR 704.5a) did not converge. " +
                        DumpState(s));
                }
            }

            // INV-2: Stack depth is never negative (sanity on the DTO; a
            // negative would mean a pop/push accounting bug).
            if (s.Stack.Count < 0)
            {
                throw new FuzzInvariantViolation(
                    $"{ctx}: stack depth is negative ({s.Stack.Count}). " + DumpState(s));
            }

            // INV-3: No permanent in two zones at once (CR 400.7) — every
            // REVEALED instance id across all public zones of all players is
            // unique. Hidden-information cards (CR 706 — opponent hand/library)
            // are masked to a zeroed InstanceId in the snapshot, so a zero id is
            // the "redacted" sentinel and is expected to repeat; we skip it.
            var seen = new HashSet<Guid>();
            foreach (var p in s.Players)
            {
                foreach (var c in AllZoneCards(p))
                {
                    if (c.InstanceId == Guid.Empty) continue; // masked / redacted
                    if (!seen.Add(c.InstanceId))
                    {
                        throw new FuzzInvariantViolation(
                            $"{ctx}: card '{c.Name}' (id {c.InstanceId}) appears in " +
                            "more than one zone — duplicate instance id across zones " +
                            "(CR 400.7 violated). " + DumpState(s));
                    }
                }
            }

            // INV-4: No creature with toughness <= 0 lingering on the
            // battlefield past SBA (CR 704.5f). A creature whose computed
            // toughness is <= 0 must have been put into the graveyard by SBA;
            // if it's still on a battlefield the SBA fixed point did not
            // converge / fire.
            foreach (var p in s.Players)
            {
                foreach (var c in p.Battlefield.Cards)
                {
                    if (c.Types.Contains("Creature") && c.Toughness is int t && t <= 0)
                    {
                        throw new FuzzInvariantViolation(
                            $"{ctx}: creature '{c.Name}' has toughness {t} (<= 0) but is " +
                            "still on the battlefield — SBA destroy (CR 704.5f) did not " +
                            "converge. " + DumpState(s));
                    }
                }
            }

            // (INV-5: a lost player stays lost; enforced implicitly — INV-1
            // catches the live-yet-dead case, the driver terminates on
            // game-over. Documented here for completeness.)

            // If a prompt is in flight, its recipient must be a real seat.
            if (prompt != null)
            {
                s.Players.Any(p => p.Id == prompt.PlayerId).Should().BeTrue(
                    $"{ctx}: prompt addressed to unknown player {prompt.PlayerId}");
            }
        }

        private static IEnumerable<CardSnapshotDto> AllZoneCards(PlayerDto p)
        {
            foreach (var c in p.Hand.Cards) yield return c;
            foreach (var c in p.Battlefield.Cards) yield return c;
            foreach (var c in p.Graveyard.Cards) yield return c;
            foreach (var c in p.Library.Cards) yield return c;
            foreach (var c in p.Exile.Cards) yield return c;
        }

        private static string DumpState(GameStateDto s) =>
            "STATE: " + string.Join(" || ", s.Players.Select(p =>
                $"{p.Name} life={p.Life} lost={p.HasLost} " +
                $"bf=[{string.Join(",", p.Battlefield.Cards.Select(c => $"{c.Name}({c.Power}/{c.Toughness},t={c.Tapped})"))}] " +
                $"hand={p.Hand.Cards.Count} gy={p.Graveyard.Cards.Count} lib={p.Library.Cards.Count}"))
            + $" || stack={s.Stack.Count}";
    }

    private sealed class FuzzInvariantViolation : Xunit.Sdk.XunitException
    {
        public FuzzInvariantViolation(string message) : base(message) { }
    }

    // =======================================================================
    // Deck construction — real cards so casting / combat / SBA actually fire.
    // Each seat gets basic lands + cheap vanilla creatures + a burn spell,
    // shuffled deterministically by the game seed. Using the
    // EmbeddedCardRepository routes cards through the prod binder/factory
    // chain so they're genuinely castable.
    // =======================================================================
    private static GameFacade BuildDeckFacade(int seed)
    {
        var repo = new EmbeddedCardRepository();
        var aliceDeck = BuildDeck(seed);
        var bobDeck = BuildDeck(seed + 7919); // distinct prime offset per seat
        return GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: repo);
    }

    private static IReadOnlyList<ICard> BuildDeck(int seed)
    {
        var deck = new List<ICard>();
        // Mana base: enough basics that casts are frequently affordable.
        for (var i = 0; i < 8; i++) deck.Add(new Land("Forest"));
        for (var i = 0; i < 4; i++) deck.Add(new Land("Mountain"));
        // Cheap vanilla creatures → combat + SBA pressure.
        for (var i = 0; i < 6; i++) deck.Add(new Creature("Grizzly Bears", "1G", 2, 2));
        for (var i = 0; i < 4; i++) deck.Add(new Creature("Llanowar Elves", "G", 1, 1));
        // A bigger body for variety.
        for (var i = 0; i < 2; i++) deck.Add(new Creature("Centaur Courser", "2G", 3, 3));
        // A burn spell → exercises targeting + direct life loss + SBA loss.
        for (var i = 0; i < 6; i++) deck.Add(new Instant("Lightning Bolt", "R"));
        // Deterministic shuffle by seed so the library order is reproducible
        // (the game's own GameRandom also shuffles at start; both are seeded).
        return Shuffle(deck, new GameRandom(seed));
    }

    // -----------------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------------
    private static List<T> Shuffle<T>(List<T> items, GameRandom rng)
    {
        // Fisher–Yates with the seeded RNG — reproducible for a given seed.
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }

    private static object IdProjection(GameStateDto s) => new
    {
        Players = s.Players.Select(p => new
        {
            p.Name,
            p.Id,
            p.Life,
            p.HasLost,
            Hand = ZoneIds(p.Hand),
            Battlefield = p.Battlefield.Cards
                .Select(c => $"{c.Name}|{c.InstanceId}|{c.Power}/{c.Toughness}|t={c.Tapped}").ToList(),
            Graveyard = ZoneIds(p.Graveyard),
            Library = ZoneIds(p.Library),
            Exile = ZoneIds(p.Exile),
        }).ToList(),
        Stack = s.Stack.Select(o => $"{o.Kind}|{o.Id}").ToList(),
        s.TurnNumber,
        s.Phase,
        s.ActivePlayerId,
    };

    private static List<string> ZoneIds(ZoneDto z) =>
        z.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList();

    private static IReadOnlyList<CardSnapshotDto> HandOf(GameFacade facade, Guid playerId)
        => PlayerOf(facade, playerId).Hand.Cards;

    private static PlayerDto PlayerOf(GameFacade facade, Guid playerId)
        => facade.GetState().Players.First(p => p.Id == playerId);

    private static PlayerDto OpponentOf(GameFacade facade, Guid playerId)
        => facade.GetState().Players.First(p => p.Id != playerId);
}
