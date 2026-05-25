using Xunit;

namespace Majik.Core.Tests;

/// <summary>
/// xUnit collection marker for test classes that mutate the process-global
/// agent / event-bus / game-random registries
/// (<see cref="Majik.Core.Players.Agents.AgentRegistry"/>,
/// <see cref="Majik.Core.Events.EventBusRegistry"/>,
/// <see cref="Majik.Core.Random.GameRandomRegistry"/>).
///
/// These registries are keyed by <c>Player.Id</c> but every test class
/// that uses them also calls <c>Clear()</c> in its constructor / Dispose
/// — and xUnit parallelises distinct test classes by default. That means
/// one class's mid-test <c>Clear()</c> can wipe an agent another class
/// just registered, surfacing as flaky "agent was not consulted" failures
/// (e.g. <c>SurveilSelfSpell_ConsultsAgent_WhenRegistered</c>).
///
/// All tests under this collection run serially with respect to each
/// other; they may still <c>Clear()</c> on entry/exit safely.
/// </summary>
[CollectionDefinition(nameof(StaticRegistryCollection))]
public class StaticRegistryCollection
{
    // Marker only — no fixture state required. The collection name string
    // is what xUnit uses to bind test classes together; see the
    // [Collection(nameof(...))] attribute on the affected test suites.
}
