# PLAN 02 — Memoize `ContinuousEffectsService.Compute` (performance)

## Goal
Eliminate redundant full layer-system recomputation in `ContinuousEffectsService.Compute`. Every `Creature.Power`/`Creature.Toughness` read runs the whole CR 613 pipeline (`ComputeStrippedSet`, `SyncAbilityGrants`, two LINQ `.Where().ToList()`, `GroupBy`/`OrderBy`, O(n²) `TopoSortByDependencies` per layer) from scratch — even with zero effects registered. Combat, the SBA fixed-point loop, and snapshotting multiply it. Deliver a zero-effect fast path and a generation-counter cache, without ever returning stale characteristics.

## Current state (file:line)
- `Majik.Core/Effects/ContinuousEffectsService.cs`: `Compute(Permanent)` `:66-155` — seed printed P/T `:75-77`, types/subtypes/keywords `:85-93`, `ComputeStrippedSet()` `:107`, `SyncAbilityGrants(stripped)` `:114` (MUTATES grant state), `.Where().Where().ToList()` `:116-121`, `GroupBy`/`OrderBy` `:124-126`, `TopoSortByDependencies` per layer `:128-134`, Layer-7c counter postlude reading `perm.Counters` `:145-152`. Mutators: `Register :22-26`, `Unregister :28-32`, `Prune :35-45`, `ExpireEndOfTurn :264-273`.
- `Majik.Core/Cards/Creature.cs`: `GetPower() :93-98`, `GetToughness() :102-107` — face-down short-circuit (returns 2), else `ActiveEffects.Compute(this).Power/Toughness` with **no memo**. `BasePower`/`BaseToughness` setters `:20-47`.
- P/T-feeding mutations: `Permanent.Counters` (`CounterCollection.Add/Remove`); `IsFaceDown` (`Permanent.cs:67-82`); base P/T setters. **Damage does NOT affect computed P/T** (`IsDead` compares `Damage >= Toughness` separately) — no bump needed.
- Multiplier callers: `CombatDamageAssigner.cs:86,116,151-155`, `CombatFlow.cs:119-162`, `StateBasedActions.cs:83-92` + `CreatureDeathCheck` (per creature per pass), `StateSnapshotter.cs:108-109`.
- Confirmed **non-reentrant today**: no effect `Apply`/CDA evaluator re-enters `Compute`/`GetPower`/`GetToughness` (CDAs read external state only — life, graveyard sets, artifact counts). Makes caching safe; cache impl must not introduce reentrancy.

## Design
### (a) Zero-effect fast path
At the top of `Compute(Permanent)`: `if (_effects.Count == 0) return BuildPrinted(permanent);` assembling printed P/T + types + subtypes + keyword markers + the 7c counter delta. Always correct (a strict simplification under the precondition). Removes the dominant cost in the common case.

### (b) Generation-counter cache
`private long _generation;` bumped on every change affecting any permanent's computed characteristics. `Dictionary<Permanent,(long gen, PermanentCharacteristics chars)> _cache` (`ReferenceEqualityComparer`). `Compute` returns cached entry if `gen == _generation`, else runs the pipeline and stores. `Creature.GetPower/GetToughness` unchanged (cache lives in the service → all ~10 call sites benefit).

**Invalidation surface (the correctness crux):**
1. Effect-set changes (service-owned): bump on `Register`/`Unregister`/`Prune`/`ExpireEndOfTurn`.
2. **Counter changes — recommended FALLBACK: exclude the 7c counter delta from the cached value**; cache only the layered result, re-apply the cheap counter arithmetic on every call. Removes counters from the invalidation surface entirely (avoids plumbing a callback into `CounterCollection`).
3. Base P/T setters + face-down (`MarkFaceDown`/`TurnFaceUp`): bump generation when `ActiveEffects != null`.
4. **External CDA inputs** (Tarmogoyf/Death's Shadow/Master of Etherium read life/graveyard/artifact counts that change without touching `_effects`): subscribe the service to `CardMovedEvent` + `LifeChangedEvent` (+ control/counter events) and bump on each. Conservative default: bump liberally (any zone move, life change, counter change, effect mutation, start of each SBA pass) — the win still lands because the multipliers issue many `Compute` calls between bumps.

### Effect-type-agnostic invalidation (coordination requirement)
Cache key is `(generation, Permanent)` only — never keyed/invalidated by effect type. Any effect mutation bumps the global generation, so a NEW effect class (e.g. Layer-5 colors from #6) is auto-covered with zero changes here. State as an explicit code-comment invariant.

## Step-by-step
1. Fast path (a); extract seeding + counter postlude into shared helpers so the two paths can't diverge.
2. Tests asserting fast path ≡ full path for a creature with counters/keywords/subtypes when `_effects` empty.
3. Add `_generation`; bump in the four mutators (behaviorally a no-op until the cache exists).
4. Pull the 7c counter delta out of the cached region (apply live post-lookup).
5. Add `_cache`; lookup/store; return a defensive clone on hit (or make characteristics immutable-on-return).
6. Wire base-P/T + face-down bumps (promote `ActiveEffects` access to `Permanent` if needed).
7. Wire external-CDA invalidation via `IEventBus` (`CardMovedEvent`+`LifeChangedEvent`).
8. Audit every `.Counters.Add/Remove` and `BasePower=/BaseToughness=` site.
9. Full suite; then Api/Server/Bot.
10. `Clear()`/invalidate-all for service reuse across games (mirror `TriggerManager.Clear`).

## Test / verification strategy
Correctness: anthem +N/+N, +1/+1 / -1/-1 counters (live delta), Humility strip + `SyncAbilityGrants` reconcile, EOT expiry, layer dependency order (existing topo-sort tests unchanged), CDA freshness (Tarmogoyf after graveyard move, Death's Shadow after `LifeChangedEvent`, Master of Etherium after artifact ETB/LTB), fast-path equivalence, and a **per-surface stale-cache regression** (read P/T, mutate each surface, re-read, assert new value). Perf: BenchmarkDotNet `MemoryDiagnoser` on a wide board (30 creatures + a few anthems/CDAs) hammering `GetPower/GetToughness`; target cache-hit = dictionary lookup, per-call allocations → 0 on hits; cross-check a bot-vs-bot full-game timing.

## Risks
Stale cache (highest — a correctness bug; mitigate with conservative bumps + counter carve-out + per-surface tests); mutable returned characteristics (clone on hit or audit the ~10 sites); reentrancy (store after pipeline completes; debug guard); dictionary growth (self-invalidate by gen; clear opportunistically on Prune/Expire or use `ConditionalWeakTable`).

## Effort
Fast path (a): ~0.5 day incl. tests. Cache (b): ~1.5–2.5 days incl. invalidation wiring + per-surface tests + audit + benchmark. Total ~2–3 days; most time is the invalidation audit/tests.

## Dependencies / sequencing
Ship (a) first and independently (pure low-risk refactor + behavioral-equivalence tests). Then (b). (b) wants `IEventBus` reachable from the service for CDA invalidation; the liberal-bump-at-SBA-pass-boundary variant removes that dependency at small precision cost.

## Coordination with deferrals agent
Low overlap. **Invariant:** cache is `(generation, Permanent)`, invalidation is effect-type-agnostic — the deferrals agent does NOT touch this file when adding effect types and MUST NOT add per-type cache logic. Only coordination: if they add a NEW non-effect per-permanent P/T input (a new field like `Counters`), they must add a generation bump for it — flag this file as a touchpoint.

### Critical files
- `Majik.Core/Effects/ContinuousEffectsService.cs`, `Majik.Core/Cards/Creature.cs`
