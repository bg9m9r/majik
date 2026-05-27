using Xunit;

// Disable xUnit's per-collection parallel test execution for this assembly.
//
// Many Majik.Core test classes mutate process-global statics
// (AgentRegistry, CastingRestrictions, ZoneServiceRegistry,
// GameRandomRegistry, EventBusRegistry, etc.) without consistent
// Clear()/Dispose pairing. Under xUnit's default parallel collections
// these mutations race and surface as cross-class flaky failures —
// usually as "agent was not consulted" / "expected reorder didn't
// happen" / "restriction leaked across tests."
//
// The existing StaticRegistryCollection serializes only the ~18 classes
// explicitly marked with [Collection(nameof(StaticRegistryCollection))],
// but the actual set of classes touching shared statics is much larger
// (audited 2026-05-25: 20+ unmarked offenders, growing). Serializing
// the whole assembly is the safe-by-default posture and adds only ~5s
// to a 3s run.
//
// Follow-up: convert each offending registry to AsyncLocal<T> (or per-
// test scoped storage via an IDisposable fixture), then re-enable
// parallelism. Tracked separately.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
