# Engine architecture improvement plans

Output of a 2026-05-30 architecture review (4 parallel deep-dives) across performance and card-implementation ergonomics. Each plan is grounded with `file:line` references and carries a **Coordination with deferrals agent** section, because a concurrent agent is working the v1-deferrals backlog.

## Execution order (decided with maintainer)
Ship **#2 first** (low risk, big perf win), then **#1**, then **#3 → #8** in order. `#6` is **owned by the deferrals agent** (it _is_ deferrals #4/#10/#17) — this repo's plan is the spec hand-off, not a separate track.

| # | Plan | Theme | Effort | Deferrals-agent collision |
|---|---|---|---|---|
| [01](01-async-effects-declarative-choice.md) | Async effects + declarative `ChoiceRequest` + unified targeting | Easier cards + kills auto-pick deferral class | L–XL | 🔴 Heavy — land Slice A adapter only mid-stream; B–G after they drain |
| [02](02-ces-memoization.md) | Memoize `ContinuousEffectsService.Compute` | Performance | M (S for fast-path) | 🟡 Low — invalidation is effect-type-agnostic |
| [03](03-converge-card-definition-systems.md) | Converge 4 card-def systems → 1 + shared `Fx`/`Triggers`/`Costs` | Easier cards | M–L | 🟠 Medium — Slice 3 needs a pool freeze; depends on #1 |
| [04](04-seq-and-reducer-widening.md) | Monotonic `seq` + widen client reducer | Latency + reconnect | S + M | 🟢 Minimal |
| [05](05-event-path-alloc-cleanups.md) | EventBus/TriggerManager/SBA allocation cleanups | Performance | S–M | 🟢 Minimal (heads-up on 3 files) |
| [06](06-layer-system-primitives.md) | Layer-5 colors, supertype-grant, Enchantment-Creature dual-type | Closes deferrals #17/#4/#10 | S each | 🔴 **Owned by deferrals agent** — spec only |
| [07](07-typed-event-payloads.md) | Type the `EventDto` payloads | Dev friction | M–L | 🟢 Minimal (api-first deploy) |
| [08](08-persist-engine-state.md) | Persist engine event-log/snapshots for rehydration | Scaling/availability | L–XL | 🟢 Minimal (determinism constraint) |

## The two strategic insights
- **The auto-pick deferrals are a symptom of one root cause** (#1): `IEffect.Execute()` is synchronous, so mid-resolution choices block via `.GetAwaiter().GetResult()` and a 20-method `IPlayerAgent` that all default to `candidates[0]`. Targeting is already declarative/good but only wired into spell-cast. Fixing #1 makes "do it correctly" as cheap as "do it lazily."
- **`ContinuousEffectsService.Compute` is the dominant perf cost** (#2): runs on every P/T read (combat, every SBA fixed-point pass, every snapshot) with no memoization — even with zero effects registered.
