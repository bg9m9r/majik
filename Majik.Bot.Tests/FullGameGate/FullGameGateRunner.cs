using System.Collections.Concurrent;
using System.Diagnostics;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.Random;

namespace Majik.Bot.Tests.FullGameGate;

/// <summary>One full game of the gate: heuristic bot on both seats.</summary>
public sealed record GateGame(string DeckA, string DeckB, int Seed)
{
    public override string ToString() => $"{DeckA} vs {DeckB} seed {Seed}";
}

/// <summary>The outcome of one gate game: the distinct failure signatures it
/// produced (each at most once per game) and its wall time.</summary>
public sealed record GateGameResult(GateGame Game, IReadOnlyDictionary<string, string> Signatures, TimeSpan Elapsed, int Turns);

/// <summary>
/// Runs gate games. Each game asserts (a) its game task does not fault,
/// (b) zone integrity (CR 400.1) at every step boundary, and (c) no contained
/// engine fault was reported on its flow, and (d) no event handler threw (only
/// while <see cref="CaptureHandlerFaults"/> is open). A failure becomes a stable
/// signature: exception type + top engine frame for faults, invariant name +
/// card name for zone integrity, and the last event type for a hang (a hang has
/// no frame to key on: the stuck flow's stack is not observable from here).
/// </summary>
public static class FullGameGateRunner
{
    private static readonly ICardRepository Repo = new EmbeddedCardRepository();

    // GameFacade.ContainedFaultReported is process-wide; each game marks its
    // own async flow so a contained fault is attributed to the game it came from.
    private static readonly AsyncLocal<ConcurrentDictionary<string, string>?> CurrentGame = new();
    private static int _subscribed;

    private static void EnsureSubscribed()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 1) return;
        GameFacade.ContainedFaultReported += (ex, ctx) =>
        {
            var sigs = CurrentGame.Value;
            if (sigs is null) return; // not a gate game (another test's flow)
            sigs.TryAdd(Signature("ContainedFault", ex), $"{ctx}: {ex.Message}");
        };
    }

    private static readonly object HookLock = new();
    private static int _hookDepth;
    private static Action<Exception, GameEvent>? _previousHook;

    /// <summary>
    /// (d) While the returned scope is open, an exception thrown by an event
    /// handler in a gate game becomes a <c>HandlerFault|ExceptionType|top Majik
    /// frame</c> signature on that game. The bus isolates handlers and routes
    /// the exception to the process-wide GameFacade.OnEventHandlerError, which
    /// otherwise swallows it in Release and crashes the process in Debug.
    /// Attributed like contained faults, through the game's AsyncLocal (the
    /// hook runs synchronously on the publishing flow, or on a faulted async
    /// handler's continuation, which carries the same ExecutionContext).
    /// Faults on other flows go to the hook that was installed before. Scopes
    /// nest (a counter); the last one to close restores that hook.
    /// </summary>
    public static IDisposable CaptureHandlerFaults()
    {
        lock (HookLock)
        {
            if (_hookDepth++ == 0)
            {
                var previous = _previousHook = GameFacade.OnEventHandlerError;
                GameFacade.OnEventHandlerError = (ex, e) =>
                {
                    if (CurrentGame.Value is { } sigs)
                    {
                        sigs.TryAdd(Signature("HandlerFault", ex), $"{e.GetType().Name}: {ex.Message}");
                        return;
                    }
                    if (previous is not null) { previous(ex, e); return; }
#if DEBUG
                    // No previous hook: keep GameFacade's Debug default (fail loud).
                    var captured = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                    ThreadPool.QueueUserWorkItem(_ => captured.Throw());
#endif
                };
            }
        }
        return new Restore(() =>
        {
            lock (HookLock)
            {
                if (--_hookDepth == 0)
                {
                    GameFacade.OnEventHandlerError = _previousHook;
                    _previousHook = null;
                }
            }
        });
    }

    private sealed class Restore(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    /// <summary>The gate's verdict over a set of results. <paramref name="allowList"/>
    /// maps each tolerated signature to whether it is intermittent (exempt from
    /// the vanished check).</summary>
    public static (IReadOnlyList<string> Unlisted, IReadOnlyList<string> Vanished) Evaluate(
        IEnumerable<GateGameResult> results, IReadOnlyDictionary<string, bool> allowList)
    {
        var bySignature = results
            .SelectMany(r => r.Signatures.Keys.Select(s => (Signature: s, r.Game)))
            .GroupBy(x => x.Signature)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        var unlisted = bySignature.Where(g => !allowList.ContainsKey(g.Key))
            .Select(g => $"{g.Key} ({g.Count()} games, e.g. {g.First().Game})").ToList();
        var vanished = allowList.Where(kv => !kv.Value).Select(kv => kv.Key)
            .Except(bySignature.Select(g => g.Key)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return (unlisted, vanished);
    }

    public static async Task<GateGameResult> RunAsync(GateGame game, int maxTurns, TimeSpan timeout,
        Action<GameFacade>? configure = null)
    {
        EnsureSubscribed();
        var sigs = new ConcurrentDictionary<string, string>();
        CurrentGame.Value = sigs;
        var sw = Stopwatch.StartNew();
        int turns = 0;
        try
        {
            using var ids = DeterministicIdScope.Push(new DeterministicIdSource(game.Seed));
            var facade = GameFacade.Create(
                $"{game.DeckA}-A", $"{game.DeckB}-B",
                Deck(game.DeckA), Deck(game.DeckB), cardRepo: Repo);
            facade.ReplaceAliceAgent(new HeuristicBotAgent(cardRepository: Repo));
            facade.ReplaceBobAgent(new HeuristicBotAgent(cardRepository: Repo));
            var players = new[] { facade.Alice, facade.Bob };
            configure?.Invoke(facade);

            // (b) CR 400.1 at every step boundary. A read-only walk of the zones.
            string lastEvent = "";
            int lastTurn = 0;
            long eventCount = 0;
            string lastDetail = "";
            facade.EventBus.SubscribeAll(e =>
            {
                eventCount++;
                lastEvent = e.GetType().Name; // a hang's signature (no stack of the stuck flow is available)
                if (e is Majik.Core.Domain.DomainEvents.StateBasedActionExecutedEvent sba) lastDetail = sba.ActionDescription;
                if (e is TurnStartedEvent ts) lastTurn = ts.TurnNumber;
                if (e is not StepStartedEvent) return;
                foreach (var d in ZoneIntegrity.FindDuplicates(players))
                    sigs.TryAdd($"ZoneIntegrity|{d.Card.Name}", $"{d.FirstZone} and {d.SecondZone}");
            });

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await facade.StartFullGameAsync(firstPlayerSlot: game.Seed % 2, maxTurns: maxTurns,
                    ct: cts.Token, rng: new GameRandom(game.Seed)).ConfigureAwait(false);
                var result = await facade.FullGameTask!.ConfigureAwait(false);
                turns = result.TurnsPlayed;
                // The engine can absorb the cancellation and end the game with
                // a normal result; a game that needed the timeout is still a hang.
                if (cts.IsCancellationRequested) sigs.TryAdd($"Timeout|{lastEvent}", $"{game} exceeded {timeout.TotalSeconds:F0}s on turn {lastTurn} after {eventCount} events; last: {lastDetail}");
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                sigs.TryAdd($"Timeout|{lastEvent}", $"{game} exceeded {timeout.TotalSeconds:F0}s on turn {lastTurn} after {eventCount} events; last: {lastDetail}");
            }
            catch (Exception ex)
            {
                // (a) the game task faulted.
                sigs.TryAdd(Signature("GameFault", ex), ex.Message);
            }
            foreach (var d in ZoneIntegrity.FindDuplicates(players))
                sigs.TryAdd($"ZoneIntegrity|{d.Card.Name}", $"{d.FirstZone} and {d.SecondZone}");
        }
        catch (Exception ex)
        {
            // Setup itself threw (deck build / Create).
            sigs.TryAdd(Signature("SetupFault", ex), ex.Message);
        }
        finally
        {
            CurrentGame.Value = null;
        }
        sw.Stop();
        return new GateGameResult(game, new Dictionary<string, string>(sigs), sw.Elapsed, turns);
    }

    private static List<ICard> Deck(string archetype) =>
        Majik.Bot.Decks.BotDeckCatalog.Get(archetype)
            .Select(n => DeckCardShellBuilder.Build(Repo.GetByName(n)
                ?? throw new InvalidOperationException($"not in seed: {n}")))
            .ToList();

    /// <summary>"kind|ExceptionType|top engine frame". The message is left out:
    /// it carries per-game ids and would make the signature unstable.</summary>
    public static string Signature(string kind, Exception ex)
    {
        while (ex is AggregateException { InnerException: { } inner }) ex = inner;
        while (ex is System.Reflection.TargetInvocationException { InnerException: { } inner2 }) ex = inner2;
        return $"{kind}|{ex.GetType().Name}|{TopEngineFrame(ex)}";
    }

    /// <summary>The first stack frame inside Majik (not this harness),
    /// with async state machines and closures folded back to their method.</summary>
    public static string TopEngineFrame(Exception ex)
    {
        foreach (var frame in new StackTrace(ex, false).GetFrames())
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (method is null || type is null) continue;
            var name = method.Name;
            // <Method>d__12.MoveNext / <>c__DisplayClass.<Method>b__0 → Type.Method
            while (type.Name.StartsWith('<') && type.DeclaringType is { } outer)
            {
                if (!type.Name.StartsWith("<>")) name = Between(type.Name);
                type = outer;
            }
            if (name.StartsWith('<')) name = Between(name);
            var full = type.FullName ?? type.Name;
            if (!full.StartsWith("Majik.", StringComparison.Ordinal)) continue;
            if (full.StartsWith("Majik.Bot.Tests", StringComparison.Ordinal)) continue;
            return $"{full}.{name}";
        }
        return "<no engine frame>";
    }

    private static string Between(string s)
    {
        int a = s.IndexOf('<'), b = s.IndexOf('>');
        return a >= 0 && b > a + 1 ? s.Substring(a + 1, b - a - 1) : s;
    }
}
