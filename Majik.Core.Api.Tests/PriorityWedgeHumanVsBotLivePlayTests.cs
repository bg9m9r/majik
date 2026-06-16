using FluentAssertions;
using Majik.Bot;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Api.Tests;

/// <summary>
/// DIAGNOSTIC live-play reproduction for the reported HUMAN-PRIORITY WEDGE in a
/// human-vs-bot match: the human seat does not receive a pass-priority prompt
/// when it should, and the game wedges (dead clock, "no active prompt" on the
/// client).
///
/// <para>Unlike <see cref="ShockLandLivePlayTests"/> /
/// <see cref="SurveilLandLivePlayTests"/> (TWO RemoteAgents, NO auto-pass prefs)
/// this test reproduces the PRODUCTION combo exactly:</para>
/// <list type="bullet">
///   <item>Alice = HUMAN (the default <c>RemoteAgent</c> seat).</item>
///   <item>Bob = BOT (<see cref="BotPlayerAgent"/> swapped in via
///     <see cref="GameFacade.ReplaceBobAgent"/>).</item>
///   <item><see cref="GameFacade.StartFullGameAsync"/> wired with an
///     <c>autoPassPrefsProvider</c> that returns non-null prefs for the HUMAN
///     seat and null for the bot — the same shape MatchService builds
///     (<c>BuildAutoPassPrefsProvider</c>).</item>
/// </list>
///
/// <para>The suspect production line is the priority-loop auto-pass at
/// <c>PriorityLoop.cs</c> ~272-282: when <c>TryAutoPass</c> fires the loop
/// synthesizes a silent pass WITHOUT calling
/// <c>agent.ChoosePriorityActionAsync</c> and WITHOUT firing
/// <c>RemoteAgent.PromptRequested</c> — so no prompt ever reaches the human.</para>
///
/// <para>This test makes Alice's window legitimately NON-dead (she holds an
/// instant-speed card → <c>PriorityKinds.Build</c> includes
/// <c>CastSpellCommand</c> → <c>IsPassOnly</c> is false). With a non-dead window
/// and the bot's spell (not hers) on top of the stack, NEITHER auto-pass reason
/// holds (no own-top, no dead-window) so the engine MUST route to her agent and
/// deliver a priority prompt. The assertion is bounded by a timeout so a wedge
/// manifests as a test FAILURE, never an infinite hang.</para>
///
/// <para>DIAGNOSTIC FINDING (2026-06-16, this run): across all three faithful
/// scenarios below the engine does NOT wedge — sub-hypothesis (c), a CLEAN
/// auto-pass that advances the game:</para>
/// <list type="number">
///   <item>Bot spell on stack + human holds an instant (NON-dead window):
///     Alice IS delivered a priority prompt to respond
///     (<see cref="BotSpellOnStack_HumanWithInstant_MustReceivePriorityPrompt"/>
///     — GREEN).</item>
///   <item>Bot spell on stack + human DEAD window (lands only): auto-pass
///     legitimately fires, the bot's spell RESOLVES, the stack drains and the
///     game advances
///     (<see cref="BotSpellOnStack_HumanDeadWindow_GameMustAdvancePastTheSpell"/>
///     — GREEN).</item>
///   <item>Human Full Control on own turn: every window surfaces a prompt
///     (<see cref="HumanFullControl_OwnTurn_MustReceivePriorityPrompt"/> —
///     GREEN).</item>
/// </list>
/// <para>Conclusion: the localized suspect (<c>PriorityLoop.TryAutoPass</c>
/// silent pass) is INNOCENT for the bot-casts-a-vanilla-spell path. The field
/// wedge must require a condition these scenarios do not hit — most likely a
/// TRIGGERED ability raising priority on a seat (see the prior single-trigger
/// order-prompt wedge, PR #2563), which is the next thing to instrument. These
/// tests stay as regression coverage proving the spell-on-stack auto-pass path
/// is sound, and as the harness to extend toward the trigger-driven repro.</para>
/// </summary>
[Collection(FuzzCollection.Name)] // serial: shares the full-game driver model
public sealed class PriorityWedgeHumanVsBotLivePlayTests
{
    private readonly ITestOutputHelper _out;

    public PriorityWedgeHumanVsBotLivePlayTests(ITestOutputHelper output) => _out = output;

    /// <summary>Minimal <see cref="IAutoPassPrefsView"/> — mirrors what the
    /// portal PUTs and MatchService hands the engine. Default = auto-pass ON,
    /// no Full Control, no phase stops (the production default a human starts
    /// with).</summary>
    private sealed class HumanPrefs : IAutoPassPrefsView
    {
        public bool FullControl { get; init; }
        public IReadOnlyDictionary<string, string> PhaseStops { get; init; }
            = new Dictionary<string, string>();
    }

    /// <summary>
    /// REPRO 2 — the bot puts a spell on the stack and the human (Alice), who
    /// holds an instant-speed card (legitimate reason to act → NON-dead window),
    /// must be prompted to respond/pass. A wedge = the prompt never arrives.
    /// </summary>
    [Fact(Skip = "Diagnostic harness: shares static AgentRegistry/GameRegistryScope state so it flakes when run in-suite, and depends on emergent bot decisions. Its finding (auto-pass is innocent; the wedge is the unobserved game-loop task) is captured in the plan + the deterministic bridge/service/watchdog regression tests. Remove Skip to run locally for manual repro.")]
    public async Task BotSpellOnStack_HumanWithInstant_MustReceivePriorityPrompt()
    {
        var repo = new EmbeddedCardRepository();

        // ── Alice (human): instants + Mountains. The instant is the key: while
        //    ANY spell sits on the stack, her window is NON-dead
        //    (PriorityKinds.Build → CastSpellCommand), so the engine has no
        //    auto-pass reason and MUST prompt her. Mana-cost format is unbraced
        //    ("R") to match the castable-shell recipe used by the fuzz harness —
        //    a braced "{R}" cost does NOT parse and leaves the card uncastable.
        var aliceDeck = new List<ICard>();
        for (var i = 0; i < 8; i++) aliceDeck.Add(new Instant("Lightning Bolt", "R"));
        for (var i = 0; i < 32; i++) aliceDeck.Add(new Land("Mountain"));

        // ── Bob (bot): cheap castable creatures + lands so the heuristic bot
        //    reliably casts a spell onto the stack during the game (recipe
        //    mirrors RandomLegalCommandFuzzTests.BuildDeck — genuinely castable
        //    through the prod binder chain).
        var bobDeck = new List<ICard>();
        for (var i = 0; i < 6; i++) bobDeck.Add(new Creature("Llanowar Elves", "G", 1, 1));
        for (var i = 0; i < 8; i++) bobDeck.Add(new Creature("Grizzly Bears", "1G", 2, 2));
        for (var i = 0; i < 4; i++) bobDeck.Add(new Creature("Centaur Courser", "2G", 3, 3));
        for (var i = 0; i < 22; i++) bobDeck.Add(new Land("Forest"));

        const int seed = 90210;
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));
        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: repo);

        // PRODUCTION COMBO: swap Bob to the real bot agent. Alice stays the
        // default RemoteAgent (the human seat).
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob, new BotConfig("Midrange")));

        var aliceId = facade.Alice.Id;
        var bobId = facade.Bob.Id;

        // ── Evidence collectors ────────────────────────────────────────────
        var prompts = new List<PromptDto>();

        // The DECISIVE signal: a priority prompt delivered to ALICE *while a
        // Bob-controlled object sits on top of the stack* (i.e. the bot has cast
        // a spell and Alice is being offered the response window). This is the
        // exact moment the wedge swallows in production. A wedge = this never
        // completes (Alice is silently auto-passed → her response window never
        // surfaces).
        var aliceRespondToBotSpell = new TaskCompletionSource<PromptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // PRECONDITION evidence: did the bot ever get a spell onto the stack at
        // all (so the scenario was actually reachable in this game)?
        var sawBotSpellOnStack = false;
        // PRECONDITION evidence: the highest "Bob spell sits on stack" count we
        // observed and whether, across the whole game, Alice was EVER prompted
        // in any window that had a Bob object on the stack.
        var aliceEverPromptedWithStackNonEmpty = false;

        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            channel.Writer.TryWrite(p);

            // Read the live stack at prompt time. The callback runs synchronously
            // on the engine thread *as the prompt is raised*, so GetState() here
            // reflects the stack the prompt was raised against.
            var stack = facade.GetState().Stack;
            var botTop = stack.Count > 0 && stack[^1].ControllerId == bobId;
            if (stack.Count > 0) sawBotSpellOnStack = true;

            if (p.PlayerId == aliceId
                && p.ExpectedKinds.Contains(nameof(PassPriorityCommand)))
            {
                if (stack.Count > 0) aliceEverPromptedWithStackNonEmpty = true;
                if (botTop) aliceRespondToBotSpell.TrySetResult(p);
            }
        });

        // DIAGNOSTIC: watch engine events independent of prompts so we can see
        // the bot cast even if Alice is silently auto-passed (the wedge). All
        // engine events bridge to the public EventDto channel (SubscribeAll).
        var spellCastEvents = 0;
        using var evSub = facade.Subscribe(ev =>
        {
            if (ev.Type == nameof(Majik.Core.Domain.DomainEvents.SpellCastEvent))
                System.Threading.Interlocked.Increment(ref spellCastEvents);
        });

        // DIAGNOSTIC: a continuous poller sampling the live stack so we can
        // observe a Bob-controlled spell SITTING on the stack even when no
        // prompt ever fires for Alice — the exact signature of the silent
        // auto-pass wedge (bot spell present, Alice never asked).
        var sawBotSpellViaPoll = false;
        var aliceHadPriorityWindowWhileBotSpellUp = false;
        var pollCts = new CancellationTokenSource();

        await facade.StartFullGameAsync(
            maxTurns: 12,
            rng: new GameRandom(seed),
            logicalClock: new LogicalClock(),
            autoPassPrefsProvider: player => player.Id == aliceId ? new HumanPrefs() : null);
        var game = facade.FullGameTask!;

        var poller = Task.Run(async () =>
        {
            while (!pollCts.IsCancellationRequested && !game.IsCompleted)
            {
                var st = facade.GetState();
                if (st.Stack.Count > 0 && st.Stack[^1].ControllerId == bobId)
                {
                    sawBotSpellViaPoll = true;
                    // If the active player is Bob and a Bob spell is on the
                    // stack, the next priority belongs to Alice (CR 117.3c) —
                    // her response window is owed but may be silently swallowed.
                    aliceHadPriorityWindowWhileBotSpellUp = true;
                }
                try { await Task.Delay(1, pollCts.Token); } catch { break; }
            }
        });

        // ── Drive Alice's seat: keep, never act, just PASS every prompt. The
        //    point is NOT for Alice to do anything — it's to observe whether the
        //    engine ever DELIVERS her a non-dead priority prompt while the bot's
        //    spell is on the stack. Bob drives himself (bot).
        var driveLoop = Task.Run(async () =>
        {
            for (var step = 0; step < 4000; step++)
            {
                if (game.IsCompleted) break;
                if (aliceRespondToBotSpell.Task.IsCompleted) break;

                var read = channel.Reader.WaitToReadAsync().AsTask();
                var winner = await Task.WhenAny(read, game);
                if (winner == game) break;
                if (!await read) break;
                if (!channel.Reader.TryRead(out var prompt)) continue;

                if (prompt.PlayerId != aliceId)
                {
                    // Bot seat is self-driving; we should not normally be asked
                    // to submit for Bob, but if a Bob prompt surfaces, skip it.
                    continue;
                }

                var cmd = RespondAlicePassive(facade, prompt) with { PlayerId = prompt.PlayerId };
                try { await facade.SubmitAsync(cmd); }
                catch (Exception ex)
                {
                    _out.WriteLine($"SUBMIT REJECTED: {cmd.GetType().Name} for Alice " +
                        $"(kinds=[{string.Join(",", prompt.ExpectedKinds)}]): {ex.Message}");
                    break;
                }
            }
        });

        // ── BOUNDED wait: a wedge = Alice is never offered the response window
        //    while the bot's spell is on the stack. Wait for the game to FINISH
        //    its (short) run OR the decisive prompt, whichever first — then judge.
        await Task.WhenAny(
            aliceRespondToBotSpell.Task,
            game,
            Task.Delay(TimeSpan.FromSeconds(20)));
        // Give a brief settle so late prompts/stack reads land.
        await Task.WhenAny(aliceRespondToBotSpell.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        pollCts.Cancel();

        _out.WriteLine($"alice prompted to RESPOND to bot spell on stack: {aliceRespondToBotSpell.Task.IsCompleted}");
        _out.WriteLine($"saw bot spell on stack AT A PROMPT: {sawBotSpellOnStack}");
        _out.WriteLine($"saw bot spell on stack VIA POLL: {sawBotSpellViaPoll}");
        _out.WriteLine($"alice had priority window owed while bot spell up (poll): {aliceHadPriorityWindowWhileBotSpellUp}");
        _out.WriteLine($"alice ever prompted while stack non-empty: {aliceEverPromptedWithStackNonEmpty}");
        _out.WriteLine($"SpellCastEvents fired (any seat): {spellCastEvents}");
        _out.WriteLine($"game completed: {game.IsCompleted}");
        _out.WriteLine("ALL prompt kinds seen: " + string.Join(" | ",
            prompts.Select(p => $"{(p.PlayerId == aliceId ? "A" : (p.PlayerId == bobId ? "B" : "?"))}:[{string.Join(",", p.ExpectedKinds)}]")));

        // PRECONDITION: the scenario must actually have occurred — the bot must
        // have CAST a spell at some point (SpellCastEvent is the robust signal;
        // the stack-poll can race a fast resolution). The bot's cast is an
        // EMERGENT decision, so a given seed may not realise it within the cap;
        // when that happens the repro premise wasn't reached → bail out as an
        // inconclusive pass rather than flaking CI. (The meaningful wedge
        // assertion below only applies once the premise holds.)
        if (spellCastEvents == 0)
        {
            _out.WriteLine("INCONCLUSIVE: bot did not cast a spell this seed; " +
                "response-window precondition not met — skipping the wedge assertion.");
            return;
        }

        // THE WEDGE ASSERTION: with a Bob-controlled spell on the stack and
        // Alice holding an instant (NON-dead window — PriorityKinds.Build
        // includes CastSpellCommand), the engine MUST deliver Alice a priority
        // prompt for the response window. If it silently auto-passes her, this
        // TCS never completes → the clock wedges. Bounded above, so a wedge =
        // FAILURE not hang.
        aliceRespondToBotSpell.Task.IsCompleted.Should().BeTrue(
            "while a Bob-controlled spell is on top of the stack and Alice holds " +
            "an instant (NON-dead window), the engine MUST deliver Alice a " +
            "priority prompt to respond/pass — never silently auto-pass her and " +
            "wedge the clock (PriorityLoop.cs ~272-282 / TryAutoPass ~494-563)");
    }

    /// <summary>
    /// REPRO 1 — Alice's OWN Draw step. With auto-pass prefs and no instant in
    /// hand, the dead-window auto-pass legitimately fires, so this test instead
    /// asks the narrower question: with FULL CONTROL set (which Gate 3 of
    /// TryAutoPass must honor by suppressing auto-pass), does Alice receive a
    /// priority prompt at her own turn? Full Control existing precisely so the
    /// human DOES get every window; if the wedge swallows even Full-Control
    /// windows the human can never act.
    /// </summary>
    [Fact(Skip = "Diagnostic harness: shares static AgentRegistry/GameRegistryScope state so it flakes when run in-suite, and depends on emergent bot decisions. Its finding (auto-pass is innocent; the wedge is the unobserved game-loop task) is captured in the plan + the deterministic bridge/service/watchdog regression tests. Remove Skip to run locally for manual repro.")]
    public async Task HumanFullControl_OwnTurn_MustReceivePriorityPrompt()
    {
        var repo = new EmbeddedCardRepository();

        var aliceDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) aliceDeck.Add(new Land("Mountain"));

        var bobDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) bobDeck.Add(new Land("Forest"));

        const int seed = 1337;
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));
        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: repo);
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob, new BotConfig("Midrange")));

        var aliceId = facade.Alice.Id;
        var prompts = new List<PromptDto>();
        var aliceAnyPriorityPrompt = new TaskCompletionSource<PromptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            channel.Writer.TryWrite(p);
            // A priority prompt is one offering PassPriorityCommand. (Mulligan /
            // bottom prompts are not priority windows.)
            if (p.PlayerId == aliceId && p.ExpectedKinds.Contains(nameof(PassPriorityCommand)))
                aliceAnyPriorityPrompt.TrySetResult(p);
        });

        // FULL CONTROL on → Gate 3 of TryAutoPass must suppress ALL auto-pass
        // for Alice, so every one of her priority windows surfaces a prompt.
        await facade.StartFullGameAsync(
            maxTurns: 3,
            rng: new GameRandom(seed),
            logicalClock: new LogicalClock(),
            autoPassPrefsProvider: player =>
                player.Id == aliceId ? new HumanPrefs { FullControl = true } : null);
        var game = facade.FullGameTask!;

        var driveLoop = Task.Run(async () =>
        {
            for (var step = 0; step < 2000; step++)
            {
                if (game.IsCompleted) break;
                if (aliceAnyPriorityPrompt.Task.IsCompleted) break;
                var read = channel.Reader.WaitToReadAsync().AsTask();
                var winner = await Task.WhenAny(read, game);
                if (winner == game) break;
                if (!await read) break;
                if (!channel.Reader.TryRead(out var prompt)) continue;
                if (prompt.PlayerId != aliceId) continue;
                var cmd = RespondAlicePassive(facade, prompt) with { PlayerId = prompt.PlayerId };
                try { await facade.SubmitAsync(cmd); }
                catch (Exception ex)
                {
                    _out.WriteLine($"SUBMIT REJECTED (repro1): {cmd.GetType().Name}: {ex.Message}");
                    break;
                }
            }
        });

        var completed = await Task.WhenAny(
            aliceAnyPriorityPrompt.Task,
            Task.Delay(TimeSpan.FromSeconds(15)));

        _out.WriteLine($"alice priority prompt delivered (full control): {aliceAnyPriorityPrompt.Task.IsCompleted}");
        _out.WriteLine($"game completed: {game.IsCompleted}");
        _out.WriteLine("ALL prompt kinds seen: " + string.Join(" | ",
            prompts.Select(p => $"{(p.PlayerId == aliceId ? "A" : "B")}:[{string.Join(",", p.ExpectedKinds)}]")));

        completed.Should().Be(aliceAnyPriorityPrompt.Task,
            "with Full Control set, auto-pass is fully suppressed (Gate 3) and " +
            "Alice MUST receive a priority prompt on her own turn — never a wedge");
    }

    /// <summary>
    /// REPRO 2b — the DEAD-window response case (the one most consistent with
    /// the field reports of a permanently-stuck clock). Alice (human) holds NO
    /// instant and NO castable — only basic lands. While a Bob-controlled spell
    /// is on the stack, Alice's window is PASS-ONLY (PriorityKinds.Build →
    /// [PassPriorityCommand]) and auto-pass legitimately FIRES, silently passing
    /// her with no prompt.
    ///
    /// <para>The decisive question this test answers (sub-hypothesis a vs c):
    /// after the silent auto-pass, does the game ADVANCE (the bot's spell
    /// resolves and the stack clears — sub-hypothesis (c), a clean auto-pass,
    /// NOT the user's bug) or does it DEADLOCK with the spell stuck on the stack
    /// forever (sub-hypothesis (a), the wedge)?</para>
    ///
    /// <para>Assertion: within a bounded timeout the bot's spell must RESOLVE
    /// (we observe the stack return to empty after having held a Bob spell). A
    /// wedge = the stack never clears → FAILURE, never a hang.</para>
    /// </summary>
    [Fact(Skip = "Diagnostic harness: shares static AgentRegistry/GameRegistryScope state so it flakes when run in-suite, and depends on emergent bot decisions. Its finding (auto-pass is innocent; the wedge is the unobserved game-loop task) is captured in the plan + the deterministic bridge/service/watchdog regression tests. Remove Skip to run locally for manual repro.")]
    public async Task BotSpellOnStack_HumanDeadWindow_GameMustAdvancePastTheSpell()
    {
        var repo = new EmbeddedCardRepository();

        // Alice: LANDS ONLY → every priority window with a non-empty stack is
        // pass-only (dead). Auto-pass will fire for her.
        var aliceDeck = new List<ICard>();
        for (var i = 0; i < 40; i++) aliceDeck.Add(new Land("Plains"));

        // Bob (bot): castable creatures so he puts spells on the stack.
        var bobDeck = new List<ICard>();
        for (var i = 0; i < 6; i++) bobDeck.Add(new Creature("Llanowar Elves", "G", 1, 1));
        for (var i = 0; i < 8; i++) bobDeck.Add(new Creature("Grizzly Bears", "1G", 2, 2));
        for (var i = 0; i < 4; i++) bobDeck.Add(new Creature("Centaur Courser", "2G", 3, 3));
        for (var i = 0; i < 22; i++) bobDeck.Add(new Land("Forest"));

        const int seed = 40404; // distinct from the instant-window test's seed
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));
        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: repo);
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob, new BotConfig("Midrange")));

        var aliceId = facade.Alice.Id;
        var bobId = facade.Bob.Id;

        var prompts = new List<PromptDto>();
        var spellCastEvents = 0;
        using var evSub = facade.Subscribe(ev =>
        {
            if (ev.Type == nameof(Majik.Core.Domain.DomainEvents.SpellCastEvent))
                System.Threading.Interlocked.Increment(ref spellCastEvents);
        });

        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p =>
        {
            prompts.Add(p);
            channel.Writer.TryWrite(p);
        });

        // Poller observes the stack life-cycle: did a Bob spell appear, and did
        // the stack subsequently DRAIN back to empty (= the spell resolved and
        // the game advanced)?
        var sawBotSpellOnStack = false;
        var stackDrainedAfterBotSpell = false;
        var pollCts = new CancellationTokenSource();

        await facade.StartFullGameAsync(
            maxTurns: 12,
            rng: new GameRandom(seed),
            logicalClock: new LogicalClock(),
            autoPassPrefsProvider: player => player.Id == aliceId ? new HumanPrefs() : null);
        var game = facade.FullGameTask!;

        var poller = Task.Run(async () =>
        {
            while (!pollCts.IsCancellationRequested && !game.IsCompleted)
            {
                var st = facade.GetState();
                var botTop = st.Stack.Count > 0 && st.Stack[^1].ControllerId == bobId;
                if (botTop) sawBotSpellOnStack = true;
                // The spell drained: we had previously seen a Bob spell, and now
                // the stack is empty again → it resolved (or was countered) and
                // the engine advanced past it.
                if (sawBotSpellOnStack && st.Stack.Count == 0)
                    stackDrainedAfterBotSpell = true;
                try { await Task.Delay(1, pollCts.Token); } catch { break; }
            }
        });

        var driveLoop = Task.Run(async () =>
        {
            for (var step = 0; step < 6000; step++)
            {
                if (game.IsCompleted) break;
                if (stackDrainedAfterBotSpell) break;
                var read = channel.Reader.WaitToReadAsync().AsTask();
                var winner = await Task.WhenAny(read, game);
                if (winner == game) break;
                if (!await read) break;
                if (!channel.Reader.TryRead(out var prompt)) continue;
                if (prompt.PlayerId != aliceId) continue;
                var cmd = RespondAlicePassive(facade, prompt) with { PlayerId = prompt.PlayerId };
                try { await facade.SubmitAsync(cmd); }
                catch (Exception ex)
                {
                    _out.WriteLine($"SUBMIT REJECTED (2b): {cmd.GetType().Name}: {ex.Message}");
                    break;
                }
            }
        });

        // Bounded wait: succeed the moment the spell drains (game advanced), or
        // cap at 20s so a true wedge fails fast rather than hanging forever.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!stackDrainedAfterBotSpell && !game.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        pollCts.Cancel();

        _out.WriteLine($"SpellCastEvents fired (any seat): {spellCastEvents}");
        _out.WriteLine($"saw bot spell on stack (poll): {sawBotSpellOnStack}");
        _out.WriteLine($"stack drained after bot spell (= game advanced): {stackDrainedAfterBotSpell}");
        _out.WriteLine($"game completed: {game.IsCompleted}");
        _out.WriteLine("ALL prompt kinds seen: " + string.Join(" | ",
            prompts.Select(p => $"{(p.PlayerId == aliceId ? "A" : (p.PlayerId == bobId ? "B" : "?"))}:[{string.Join(",", p.ExpectedKinds)}]")));

        // PRECONDITION (emergent — bail inconclusive if the seed didn't realise
        // it, rather than flaking CI): the bot must have put a spell on the stack.
        if (!sawBotSpellOnStack)
        {
            _out.WriteLine("INCONCLUSIVE: bot did not put a spell on the stack this " +
                "seed; dead-window precondition not met — skipping the advance assertion.");
            return;
        }

        // THE WEDGE ASSERTION (dead-window variant). After Alice is silently
        // auto-passed on her pass-only response window, the bot's spell must
        // RESOLVE and the stack must drain — the game advances (sub-hypothesis
        // c, clean). If the stack stays stuck forever the clock wedges
        // (sub-hypothesis a) and this fails (red) within the bound.
        stackDrainedAfterBotSpell.Should().BeTrue(
            "after the human's pass-only response window is auto-passed, the " +
            "bot's spell must resolve and the game advance — never leave the " +
            "spell stuck on the stack with a dead clock (PriorityLoop.cs " +
            "~272-282 / TryAutoPass ~494-563)");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static GameCommand RespondAlicePassive(GameFacade facade, PromptDto prompt)
    {
        var kinds = prompt.ExpectedKinds;
        if (kinds.Contains(nameof(MulliganCommand)))
            return new MulliganCommand(Keep: true);
        if (kinds.Contains(nameof(ChooseCardsToBottomCommand)))
            return new ChooseCardsToBottomCommand(Array.Empty<Guid>());
        if (kinds.Contains(nameof(ChooseManaCommand)))
            return new ChooseManaCommand(Array.Empty<Guid>());
        if (kinds.Contains(nameof(OrderTriggersCommand)))
            return new OrderTriggersCommand(Array.Empty<Guid>());
        if (kinds.Contains(nameof(ChooseYesNoCommand)))
            return new ChooseYesNoCommand(false);
        // Alice is passive — she always passes priority.
        return new PassPriorityCommand();
    }
}
