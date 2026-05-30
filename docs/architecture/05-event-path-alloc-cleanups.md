# PLAN 05 — Event-path allocation cleanups (performance)

## Goal
Remove steady-state allocation + O(permanents) overhead on the hot event-publish / trigger-eval / SBA path, with **zero behavior change** and preserved re-entrancy. Targets: (1) `EventBus.Publish` copies handler lists 2–4× per publish; (2) `TriggerManager` re-materializes `_abilities` per event + routes ~40 global `SubscribeAll` static-effect handlers through one channel that each bail per event; (3) `StateBasedActions` re-materializes `ctx.Cards` every pass + each check `OfType<Creature>().ToList()`.

## Current state (file:line)
- `Majik.Core/Events/EventBus.cs`: `Publish<T> :64-96` — `.ToArray()` at `:71,80,87,92` (up to 4 copies/publish); `PublishAsync :98-130` mirrors. Stores: `Dictionary<Type,List<Delegate>>`×2 + `List`×2 `:26-29`. `SubscribeAll/UnsubscribeAll :132-154`.
- `Majik.Core/Abilities/TriggerManager.cs`: ctor registers ONE `SubscribeAll(OnAnyEvent) :48-58`; `OnAnyEvent :314-341`; `EvaluateTriggers :136-175` does `_abilities.ToList()` `:143` per event; `EvaluateStateChangeTriggers :208-229` does `_abilities.ToList()` `:210`.
- `Majik.Core/Rules/StateBasedActions.cs`: `CheckStateBasedActions :70-95` reassigns `ctx.Cards = allCards.ToList()` every iteration `:91`; checks do `OfType<Creature>().ToList()` (e.g. `Sba/Checks/CreatureDeathCheck.cs:19`).
- 53 `SubscribeAll` call sites across ~40 static/replacement effect files (`DoesNotUntapStaticEffect`, `CursedTotemStaticEffect`, `TorporOrbStaticEffect`, `PithingNeedleStaticEffect`, `GrafdiggersCageStaticEffect`, …) each subscribe one global handler that type-checks and returns for irrelevant events.

## Design
1. **EventBus copy-on-write.** Store handler lists as swapped-on-write references (subscription churn is rare, at zone moves not per event). `Publish` reads the current reference, iterates directly, zero copy; an in-flight publish keeps its captured snapshot → re-entrancy safe, identical semantics to today's `.ToArray()`. Apply to all four channels in `Publish` + `PublishAsync`.
2. **TriggerManager snapshot + type-routed handlers.** Replace `_abilities.ToList()` (`:143,210`) with a cached snapshot array invalidated by an `_abilitiesDirty` flag set in `Register`/`Unregister`/`SyncCardRegistration`/`Clear` (snapshot captured before the loop; mid-loop mutation marks dirty for the next event). **Bigger win:** add typed-subtype routing to the bus (key on `@event.GetType()` walking up the hierarchy so `Subscribe<TBase>` still receives derived) and migrate the ~40 static-effect handlers from `SubscribeAll` to `Subscribe<TConcreteEvent>` (most care about one of `CardMovedEvent`/`SpellCastEvent`/phase events). Keep `TriggerManager.OnAnyEvent` on `SubscribeAll` initially (it legitimately needs all events).
3. **SBA shared projection.** Compute the materialized card list + creature sublist once per pass on `SbaContext`; gate the rebuild on `anyExecuted` (only rebuild when a check moved a card). Checks read the shared `IReadOnlyList<Creature>`.

## Step-by-step
1. EventBus COW (swapped-on-write stores; drop `.ToArray()`).
2. Audit publish sites (`grep -rn "\.Publish"`) for base-typed-subscription needs; add hierarchy-walk routing if needed + tests.
3. Migrate the ~40 `SubscribeAll` static effects to `Subscribe<TConcreteEvent>` (+ matching `Unsubscribe<T>` teardown), one file at a time.
4. TriggerManager `_abilitiesDirty` + snapshot.
5. `SbaContext` cached `Creatures` projection + gate rebuild on `anyExecuted`; update checks to consume it.
6. Full suite after each step (independently landable/revertible); DEBUG fail-fast preserved.

## Test / verification
No behavior change — existing ~5,975 tests are the oracle. Add/confirm re-entrancy tests (handler subscribes/unsubscribes during `Publish` matches `.ToArray()` semantics; trigger registers/unregisters during `EvaluateTriggers`; SBA check moves a card mid-loop → next pass sees update). Subtype routing tests (`Subscribe<CardMovedEvent>` fires for published `CardMovedEvent`). DEBUG: throwing handler still routes to `OnHandlerError`. Perf: `MemoryDiagnoser` publishing a representative event mix on a board with many static effects → per-publish allocations drop; irrelevant handlers no longer invoked.

## Risks
Re-entrancy semantics drift (COW preserves precisely; tests lock it); subtype-routing scope creep (audit publish sites first; migrate one file at a time); missed `Unsubscribe` on migration (pair every `Subscribe<T>` with its unsubscribe); SBA gated-rebuild correctness (every check must return true iff it changed the set — existing fixed-point already depends on this). Low overall risk.

## Effort
Medium-low. EventBus COW + SBA caching ~0.5–1 day; TriggerManager snapshot ~0.5 day; the ~40-file `SubscribeAll`→`Subscribe<T>` migration ~1–1.5 days. Total ~2–3 days, splittable.

## Dependencies / sequencing
EventBus COW first (unblocks subtype routing). SBA caching + TriggerManager snapshot independent. Handler migration depends on the routing audit. Each piece independently revertible.

## Coordination with deferrals agent
Low overlap — touches `TriggerManager`/`EventBus`/`StateBasedActions`/`SbaContext` internals; heads-up on those files suffices. **Convention to communicate:** any NEW static/replacement effect they add should `Subscribe<TEvent>` (not `SubscribeAll`) to get routing; any new `GameEvent` subtype must be published with the concrete type (or rely on hierarchy-walk).

### Critical files
- `Majik.Core/Events/EventBus.cs`, `Majik.Core/Abilities/TriggerManager.cs`, `Majik.Core/Rules/StateBasedActions.cs`
