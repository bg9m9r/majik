# PLAN 01 — Async effects + declarative `ChoiceRequest` + unify targeting across spell / ability / declarative paths

## Goal
Replace the synchronous `IEffect.Execute()` model — and the ~159 `.GetAwaiter().GetResult()` sync-over-async call sites it forces (across 139 files) — with a real async resolution path: `IEffect.ExecuteAsync(ResolutionContext)`. Collapse the 20 bespoke `IPlayerAgent.ChooseXxxAsync` prompt methods into one declarative `ChoiceRequest` (mirroring the already-good `TargetRequest`). Wire the `SpellCastFlow` targeting pipeline into the activated-ability, triggered-ability, and JSON/DSL card paths so targeting is declared once and honored everywhere. Delete the `*Stub` effect family in `EffectDefinition.cs` once the JSON path can target.

## Current state (file:line)
- **Sync effect contract.** `Majik.Core/Abilities/IEffect.cs:16` — `void Execute()`. Base impl `Majik.Core/Abilities/Effect.cs:19` wraps a plain `Action`. Resolution invokes synchronously: `ActivatedAbility.Resolve()` `ActivatedAbility.cs:126-128` and `TriggeredAbility.Resolve()` `TriggeredAbility.cs:129-131` both `foreach (effect) effect.Execute();`. `Spell` resolves the same way.
- **Resolution loop is fully synchronous.** `Majik.Core/Services/StackResolver.cs:33` `ResolveTop(...)` returns `IStackObject?`, calls `top.Resolve()` at line 76. `ResolveAll` line 223 loops synchronously. No `Task` in the resolver.
- **Sync-over-async wart.** `Majik.Core/Effects/WishTutorEffect.cs:129-135` is the exemplar: pulls the agent from `AgentRegistry.Get(caster)` and calls `agent.ChooseFromPileAsync(...).GetAwaiter().GetResult()`. No `GameContext` in the closure, so several agent methods accept `GameContext? ctx = null` (see `IPlayerAgent.cs:94-98, 110-117, 125-130, 208-213, 376-379` — explicit "sync-over-async wart; TODO: pass ctx once effects become async").
- **20-method agent with `candidates[0]` deferral defaults.** `Majik.Core/Players/Agents/IPlayerAgent.cs` — `ChooseLibraryPickAsync` (140), `ChooseGiftRecipientAsync` (171), two `ChooseYesNoAsync` (215/225), `ChooseFromHandAsync` (284), `ChooseFromBattlefieldAsync` (313), `ChooseFromPileAsync` (352), `ChooseFromRevealedAsync` (400). Each defaults to "pick first / decline."
- **Targeting is declarative and good — but only wired into spell-cast.** `Majik.Core/Players/Agents/TargetRequest.cs`. Pipeline in `SpellCastFlow.CollectTargetsAsync` `SpellCastFlow.cs:399-428`. Ability path duplicates it inline at `AbilityActivationFlow.cs:49-67`. `ActivatedAbility.TargetRequests` (`:40`), `TriggeredAbility.TargetRequests` (`:45`) already exist.
- **JSON/DSL path can't target.** `Majik.Core/CardData/Definitions/EffectDefinition.cs` ships `DealDamageStubEffectDef:47`, `DestroyTargetStubEffectDef:105`, `UntapTargetStubEffectDef:125` — no-ops "because targeting isn't ready."
- **Remote wire mapping.** `Majik.Core.Api/RemoteAgent.cs` maps each engine prompt to a wire `*Command` with a `TaskCompletionSource`; per-prompt server validation (surveil `:313-358`, revealed `:366-416`).
- **Ambient access.** `AgentRegistry` static `Player→IPlayerAgent`; no `GameContext.Current` — closures pass `null`.

## Design

### 1. `ResolutionContext` (new) — `Majik.Core/Abilities/ResolutionContext.cs`
```
public sealed record ResolutionContext(
    Player Controller,
    IPlayerAgent Agent,        // == AgentRegistry.Get(Controller) today
    GameContext Game,          // live ctx — kills the ctx==null overloads
    IReadOnlyList<IReadOnlyList<object>> ChosenTargets,
    CancellationToken Ct = default);
```
The stack object constructs it at resolve time from its `Controller` + `ChosenTargets` and the resolver-supplied `Agent`/`Game`/`Ct`.

### 2. Async effect contract + back-compat adapter
```
public interface IEffect { string Description { get; } ValueTask ExecuteAsync(ResolutionContext ctx); }
```
`Effect` keeps its sync `Action` ctor AND gains an async ctor; the legacy ctor routes through an `Action`→`ValueTask` adapter. **This is the migration lever:** every existing `new Effect("desc", () => {...})` keeps compiling and runs via the adapter. `IEffect.Execute()` is deleted, but no factory must change to keep building.

### 3. Async resolution plumbing
`ActivatedAbility`/`TriggeredAbility`/`Spell` get `ResolveAsync(agent, game, ct)` building a `ResolutionContext` and awaiting each `effect.ExecuteAsync(rc)`. `StackResolver.ResolveTop`→`ResolveTopAsync`, `ResolveAll`→`ResolveAllAsync`, threading agent+ctx from the one driver. Intervening-if recheck (`StackResolver.cs:51`) and target-legality recheck (`:60-73`) stay synchronous before `await top.ResolveAsync`.

### 4. Declarative `ChoiceRequest` (new) — replaces the 20 bespoke prompts
Mirror `TargetRequest`: `ChoiceRequest(Kind, Description, Min, Max, Candidates, Intent, Optional, CandidateGatherer)`. `IPlayerAgent` collapses to one sink `Task<IReadOnlyList<object>> ChooseAsync(GameContext, ChoiceRequest, ct)`. The legacy 20 methods become default-implemented shims over `ChooseAsync` (preserving `candidates[0]`/decline defaults), then deleted as callers migrate. Wire collapses to one `ChoiceCommand` carrying `Kind` + selected ids; the per-prompt validators fold into one `Kind`-switched validator.

### 5. Unify targeting across paths
Extract `SpellCastFlow.CollectTargetsAsync` into shared `Targeting/TargetCollection.CollectAsync(...)`. Point `SpellCastFlow`, `AbilityActivationFlow:49-67`, and the trigger path at it (one cardinality check, one gatherer contract). JSON/DSL: replace the three `*Stub` defs with real `DealDamage/DestroyTarget/UntapTarget` defs that emit a `TargetRequest` (translated from their `TargetFilter`) consumed by `TargetCollection.CollectAsync`; the effect reads `ResolutionContext.ChosenTargets`. Delete the stubs + `[JsonDerivedType]` registrations.

## Step-by-step (incremental, each slice shippable)
- **Slice A —** `ResolutionContext` + async `IEffect` + adapter (no behavior change; everything compiles via the adapter). **Ship.**
- **Slice B —** async resolver; thread agent+ctx from the driver; delete sync `Resolve()`. **Ship.**
- **Slice C —** `ChoiceRequest` + `ChooseAsync` + shimmed legacy methods; bots/RemoteAgent implement `ChooseAsync`; unified `ChoiceCommand` + folded validator. **Ship.**
- **Slice D —** migrate `WishTutorEffect` + drop the `ctx=null` overloads; migrate the remaining 138 sync-over-async files in batches grouped by prompt method. Burn-down checklist = grep `GetAwaiter().GetResult()` (159 → 0). **Ship per batch.**
- **Slice E —** extract `TargetCollection.CollectAsync`; point spell/ability/trigger at it. **Ship.**
- **Slice F —** JSON/DSL targeting + delete the `*Stub` family. **Ship.**
- **Slice G —** delete the bespoke agent methods. **Ship.**

## Test strategy
Existing ~5,975 tests are the safety net — A–B green with zero test edits. Add `ResolutionContextTests`, `EffectAdapterTests` (legacy ctor runs through `ExecuteAsync`), `ChoiceRequestTests` (mirror `TargetRequestTests`), `TargetCollection` suite reused by spell+ability+trigger. Api: `RemoteAgent` round-trip per `Kind` incl. folded validation. Determinism guard: resolve a scripted stack twice → identical.

## Risks
- **Re-entrancy/ordering** — resolve one stack object at a time, await effects sequentially; no `Task.WhenAll` over effects; rechecks stay synchronous pre-resolve.
- **DEBUG fail-fast** — async effect bodies must not throw on normal paths (`EventBus.OnHandlerError` rethrows in DEBUG).
- **Adapter masking real async needs** — a factory that should prompt but uses the sync ctor silently auto-picks; Slice D visits every site once via the grep checklist.
- **Wide blast radius** — `IEffect` touches ~1300 factories transitively; the adapter is the entire mitigation.

## Effort
A: M · B: M · C: L · D: L (parallelizable, ~139 files in S batches) · E: S · F: M · G: S. Total L–XL; only A+B+C block.

## Dependencies / sequencing
A → B → C strictly ordered. D depends on C+B. E independent after A. F depends on E+C. G depends on D draining.

## Coordination with the deferrals agent — HEAVY-COLLISION ITEM
The deferrals agent owns ~12 live worktrees, each with its own `IEffect.cs`/`RemoteAgent.cs`/`IPlayerAgent.cs` + hundreds of factories instantiating `new Effect("desc", () => {...})` and adding `GetAwaiter().GetResult()` sites. Changing `IEffect.Execute()`'s signature mid-queue breaks every open worktree on merge.

**Recommendation — land Slice A (the adapter) in a fast coordination-window PR; then pause.** Because `Effect`'s legacy `Action` ctor survives, every deferral-agent factory keeps compiling unchanged — the safe interleave point. Hold Slices B–G until the deferrals agent drains its queue (pool dry / run-cap). Do NOT delete any bespoke `IPlayerAgent` method (G) or any `*Stub` (F) while their queue is live. If forced, prefer **after they drain** for B–G.

### Critical files
- `Majik.Core/Abilities/IEffect.cs`, `Effect.cs`
- `Majik.Core/Services/StackResolver.cs`
- `Majik.Core/Players/Agents/IPlayerAgent.cs`, `TargetRequest.cs`
- `Majik.Core/Game/SpellCastFlow.cs`, `AbilityActivationFlow.cs`
- `Majik.Core.Api/RemoteAgent.cs`
