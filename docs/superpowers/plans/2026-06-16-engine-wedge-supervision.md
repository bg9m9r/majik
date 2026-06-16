# Engine Wedge Supervision (stage 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) tracking. Build/test per majik.core CLAUDE.md (`dotnet test <project>`). DCO: commit with `-s`.

**Goal:** Make a silent human-vs-bot match wedge impossible: observe the fire-and-forget game-loop task, add a watchdog for hangs, and surface a terminal error to clients — which also reveals the (currently swallowed) triggering exception for stage 2.

**Root cause (confirmed):** `MatchService.cs:828` starts `facade.StartFullGameAsync(...)` fire-and-forget; the loop (`_fullGameTask`, GameFacade.cs:1053) has **zero server-side observers**. A throw during autonomous progression (bot turn / resolution / auto-pass — between human submits) faults the unobserved task → swallowed → loop dies → human un-prompted → dead clock, no log. Only symptom logged: the human's later manual Pass → `RemoteAgent.Submit` "Agent has no pending prompt" (RemoteAgent.cs:139). Confirmed in prod logs (match 5af378c8, recurring). A pure hang (bot decision never returns) is also possible (no fault).

**Architecture decisions:**
- Supervision + watchdog live in the **singleton `MatchFacadeBridge`** (already holds the facade per match, sees every engine event = progress signal, is `Detach`-ed on every terminal path = free cancellation, already owns a scoped-MatchService callback). Lifetime-correct vs the scoped MatchService.
- Add terminal **`MatchState.Errored`** (distinct from `Completed`, which carries winner semantics).
- Single CAS `Playing → Errored` in `OnEngineErrorAsync` is the sole arbiter → double-termination races (vs concede/timeout/natural-end) are impossible.
- Watchdog = copy of `MatchTimeoutScheduler` (per-match dict + `Task.Delay` + CTS). Fires only when: loop running, no pending human prompt, no progress for `T`. Default **`Watchdog:NoProgressSeconds=90`** (config-bound). Slow-bot false-positive killed by routing the existing `onBotThinking` signal into `watchdog.Bump` (bot thinking = progress).
- SignalR terminal-error events (NOT in OpenAPI → no drift regen): `match.engine-error {matchId, reason}` (`reason` ∈ `engine-fault|engine-hang`) + `match.state-changed {state:"Errored"}`. Exception detail stays server-side (ILogger only); never on the wire.

**Tech stack:** C# / .NET 10, xUnit, FluentAssertions, SignalR. Test projects: `Majik.Server.Tests`, `Majik.Core.Api.Tests`.

---

## Task 1 — Add `MatchState.Errored`
**Files:** `Majik.Server/Matches/Match.cs:6`; audit `MatchService.AbandonAsync` terminal guard (~1094), `MatchCleanupService` finished-state enumeration, `ToDto` (~1861). Test: new `Majik.Server.Tests/Matches/MatchStateErroredTests.cs`.
- [ ] Failing test: `Errored` serializes to `"Errored"`; `AbandonAsync` on an `Errored` match no-ops/returns terminal error.
- [ ] Add `Errored` to enum; include in every terminal-state enumeration found.
- [ ] Run `dotnet test Majik.Server.Tests` (target tests). Commit `-s`.

## Task 2 — `MatchService.OnEngineErrorAsync` (terminal transition + surface)
Model on `OnTimeoutAsync` (MatchService.cs:1226). **Files:** `MatchService.cs`; new `EngineFaultReason {Fault,Hang}` enum; test `MatchServiceEngineErrorTests.cs` (use `NewServiceAndPlayingMatchAsync` + `CapturePublisher` fixtures from `MatchServiceConcedeAbandonTests`).
- [ ] Failing test (a): from Playing → `Errored`, publishes `match.engine-error` + `match.state-changed`, and exception text NOT on the wire.
- [ ] Failing test (double-termination): after `ConcedeAsync` (→Completed), `OnEngineErrorAsync` is a no-op (state stays Completed, no extra state-changed).
- [ ] Implement: load match; `State!=Playing` → return (idempotent). `LogError(fault, ...MatchId/GameId/Turn/Phase...)`. CAS `Playing→Errored` via `RetryPolicy.ExecuteAsync(_matches.TryAtomicUpdateAsync(...))`. On moved: `_timeoutScheduler?.Cancel`, `_facadeBridge?.Detach`, `_autoPassPrefs?.EvictMatch`, publish the two events. Leave facade deletion to the cleanup sweep (consistent with Timeout).
- [ ] Tests green. Commit `-s`.

## Task 3 — `MatchEngineWatchdog` component
Structural copy of `MatchTimeoutScheduler`. **Files:** new `Majik.Server/Matches/MatchEngineWatchdog.cs`; test `MatchEngineWatchdogTests.cs`.
- [ ] Failing test (b): `Arm` with tiny injected timeout, never `Bump` → `onWedged` fires once within timeout; `Arm`+repeated `Bump` faster than timeout → never fires; `Cancel` before elapse → never fires. Bounded `WaitAsync` so non-fire = test timeout.
- [ ] Implement `Arm(matchId, onWedged)`, `Bump(matchId)` (cancel+re-arm), `Cancel(matchId)`; per-match `ConcurrentDictionary<Guid,Entry>`; full fault logging in the `Task.Run` callback. Constructor `NoProgressSeconds` (default 90).
- [ ] Tests green. Commit `-s`.

## Task 4 — Expose progress/fault context on `GameFacade`
**Files:** `Majik.Core.Api/GameFacade.cs`. Pure read-only exposure, no behavior change, no OpenAPI impact.
- [ ] Add `public int CurrentTurn => _currentTurn;` and `public StepStateType CurrentPhase => _currentPhase;` (fields ~181-182). (`FullGameTask` ~1059 already present for fault classification.)
- [ ] `dotnet build Majik.Core.Api`. Commit `-s`.

## Task 5 — Wire bridge → watchdog + fault classification
**Files:** `MatchFacadeBridge.cs` (Attach ~156, ForwardEvent ~381, ForwardPrompt ~687, Detach ~200, ctor); the `onBotThinking` path (MatchService.cs:346 / IMatchHubPublisher.cs:40). Test: `MatchFacadeBridgeTests.cs` (existing fakes).
- [ ] Failing test (c): Attach → synthetic `ForwardEvent` Bumps (resets); `Detach` cancels (watchdog never fires). Positive: Attach + short timeout, no Bump → `onEngineErrored` fires once with `Hang`.
- [ ] ctor: add optional `MatchEngineWatchdog? watchdog=null` + `Func<Guid,EngineFaultReason,Exception?,CancellationToken,Task>? onEngineErrored=null` (defaults keep existing tests compiling).
- [ ] Attach: `watchdog?.Arm(matchId, classifyAndReport)` where `classifyAndReport` reads `attachment.Facade.FullGameTask` (faulted→Fault+inner exception; else loop-running & no buffered human prompt→Hang) and invokes `onEngineErrored`.
- [ ] `Bump` in ForwardEvent + ForwardPrompt (best-effort, never throws). Route `onBotThinking(true)` → `watchdog.Bump` (bot thinking = progress; kills slow-bot false-positive).
- [ ] Detach: `watchdog?.Cancel(matchId)`.
- [ ] Tests green. Commit `-s`.

## Task 6 — Fix the launch site (the discarded task) + live integration test
**Files:** `MatchService.StartGameForFirstPlayer` (MatchService.cs:817-829); new `_facadeBridge.SuperviseLoop(matchId, loopTask)` on the bridge (owns the fault continuation on the singleton). Test: extend `Majik.Core.Api.Tests/PriorityWedgeHumanVsBotLivePlayTests.cs` + a `Majik.Server.Tests` `TestAppFactory` integration test.
- [ ] Failing test: a bot/agent stub whose decision **throws** → match transitions to `Errored` + `match.engine-error` published. Hang variant: agent decision blocks on a never-completing task → watchdog fires within (test-shortened) timeout → `Errored`.
- [ ] Capture the task: `var loopTask = facade.StartFullGameAsync(...); _facadeBridge?.SuperviseLoop(match.Id, loopTask);`. `SuperviseLoop` attaches `loopTask.ContinueWith(t => { if (t.IsFaulted) <invoke onEngineErrored(Fault, t.Exception)>; }, TaskScheduler.Default)` and hands the task to the watchdog for the hang path. All on the singleton bridge (fresh DI scope per fault, per `onActivePlayerChanged` pattern). Confirmed: line 828 is the sole `StartFullGameAsync` call site.
- [ ] Tests green. Commit `-s`.

## Task 7 — DI wiring
**Files:** `Majik.Server/Composition/MatchRegistration.cs` (~60-89).
- [ ] Register `MatchEngineWatchdog` singleton, `NoProgressSeconds` bound from `config.GetValue<int>("Watchdog:NoProgressSeconds")` default 90.
- [ ] Extend `MatchFacadeBridge` registration: pass the watchdog + `onEngineErrored` callback (opens `sp.CreateScope()` → `MatchService.OnEngineErrorAsync`, mirroring `onActivePlayerChanged`/`MatchTimeoutScheduler`).
- [ ] App boots; `dotnet test Majik.Server.Tests`. Commit `-s`.

## Task 8 — Full verification
- [ ] `dotnet test Majik.Server.Tests` + `dotnet test Majik.Core.Api.Tests` green. `OpenApiContractDriftTests` still passes (no REST DTO changed). Existing `MatchServiceConcedeAbandonTests` / `MatchServiceClockTests` / `MatchFacadeBridgeTests` unaffected by the new optional ctor params.

## Risks / mitigations
- **Slow-bot false-positive:** 90s default + config-tunable + `onBotThinking`→`Bump` makes bot thinking a real progress signal.
- **Slow human turn:** watchdog suppressed while a human prompt is pending (that's `MatchTimeoutScheduler`'s domain).
- **Double-termination race:** single CAS `Playing→Errored` arbitrates; loser no-ops; Detach/Cancel idempotent.
- **Continuation lifetime:** supervision on the singleton bridge (fresh DI scope per fault), never the scoped MatchService.
- **Timer leak:** `Cancel` on every `Detach`.

## Portal contract (separate repo — note only)
Handle SignalR `match.engine-error {matchId, reason}` and `match.state-changed` state `"Errored"` as terminal: stop/clear clock, clear pending prompt, show "match aborted (engine error)". Add `"Errored"` to any TS match-state union. No OpenAPI client regen (SignalR events aren't in the contract).

## Stage 2 (after deploy)
Once stage 1 is live, the next wedge logs the real triggering exception (the card/trigger/bot path that throws during autonomous progression). Fix that specific throw.
