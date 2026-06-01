using Xunit;

// Disable xUnit's per-collection parallel test execution for this assembly.
//
// Many Majik.Core test classes mutate process-level statics without
// consistent Clear()/Dispose pairing. Under xUnit's default parallel
// collections these mutations race and surface as cross-class flaky failures
// — usually as "agent was not consulted" / "expected reorder didn't happen" /
// "restriction leaked across tests."
//
// 2026-05-31 update (scope-game-registries): the four player-keyed registries
// (AgentRegistry, ZoneServiceRegistry, GameRandomRegistry, EventBusRegistry)
// are now AsyncLocal-scoped per game (see GameRegistryScope / the
// AmbientRegistryStore<T> helper), so LIVE games no longer race on them. But
// the bulk of the unit suite constructs effects / agents directly and never
// installs a game scope, so those tests still share the process-wide FALLBACK
// store — and a re-enable probe (parallelism on) surfaced a non-deterministic
// 3–8 flaky failures per run on exactly those direct-construction paths (plus
// CastingRestrictions, which is token-/turn-scoped and deliberately left as a
// process static). The de-static of the four registries is the shipped fix;
// fully parallelising the suite needs either (a) per-test fallback isolation
// (an IDisposable fixture that pushes a fresh scope around each test) or
// (b) de-static-ing CastingRestrictions too. Tracked as a follow-up.
//
// Serializing the whole assembly remains the safe-by-default posture and adds
// only a few seconds to the run.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
