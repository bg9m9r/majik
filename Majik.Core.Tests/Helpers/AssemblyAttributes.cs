using Xunit;

// Parallel test execution is ENABLED for this assembly (xUnit's default).
//
// History
// -------
// This assembly used to be pinned to [assembly: CollectionBehavior(
// DisableTestParallelization = true)] because many test classes mutate
// process-level registries without consistent Clear()/Dispose pairing. Under
// parallel collections those mutations raced on the shared statics and
// surfaced as cross-class flaky failures — "agent was not consulted",
// "expected reorder didn't happen", "restriction leaked across tests".
//
// Two changes removed the root cause and let parallelism come back:
//
//   1. PR #1704 de-static-ed the four player-keyed registries (AgentRegistry,
//      ZoneServiceRegistry, GameRandomRegistry, EventBusRegistry) onto a
//      per-game AsyncLocal ambient store (AmbientRegistryStore<T> /
//      GameRegistryScope). This PR does the same for every remaining
//      process-global game-state registry — CastingRestrictions,
//      UntapStepRestrictions, FlashGrantRegistry, IndestructibleGrantRegistry,
//      SkipDrawRegistry, PlayerStaticAbilities, ActivatedAbilityRestrictions
//      and ControlPlayerRegistryProvider — so no registry is a shared mutable
//      static any more; each carries a per-game store plus a process-wide
//      fallback.
//
//   2. Direct-construction tests (the bulk of the suite — they build effects
//      / agents / restriction rails with no surrounding game) would otherwise
//      still share the process-wide FALLBACK store across parallel
//      collections. PerTestRegistryScopeFramework (registered below) installs
//      a FRESH GameRegistryScope.PushForGame() around EVERY test case (incl.
//      each [Theory] row) and disposes it after, so every test gets its own
//      private ambient store and cannot cross-contaminate another running
//      concurrently. No per-class edits required.
//
// With per-test fallback isolation in place, parallel collections are safe and
// materially faster, so the DisableTestParallelization pin is gone.
[assembly: TestFramework(
    "Majik.Core.Tests.Helpers.PerTestRegistryScopeFramework",
    "Majik.Core.Tests")]
