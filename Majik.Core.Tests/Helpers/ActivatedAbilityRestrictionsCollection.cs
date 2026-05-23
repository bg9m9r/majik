using Xunit;

namespace Majik.Core.Tests;

/// <summary>
/// xUnit collection marker for test classes that mutate
/// <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/> or rely on
/// it being empty (e.g. <see cref="Majik.Core.Rules.ActionValidator"/>
/// activated-ability validation tests).
///
/// The registry is process-global by design (it powers
/// Pithing-Needle-style suppression across the engine), and xUnit
/// parallelises distinct test classes by default. Predicate-driven
/// restrictions (Karn the Great Creator) can match arbitrary candidate
/// abilities, so a Karn test that's mid-flight (predicate registered,
/// not yet disposed) can leak into a concurrently-running validator
/// test and turn a "Staff" activation into a "blocked" result.
///
/// All tests under this collection run serially with respect to each
/// other; they should also call <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions.Clear"/>
/// on entry/exit to be doubly safe.
/// </summary>
[CollectionDefinition(nameof(ActivatedAbilityRestrictionsCollection))]
public class ActivatedAbilityRestrictionsCollection
{
    // Marker only — no fixture state required. The collection name string
    // is what xUnit uses to bind test classes together; see the
    // [Collection(nameof(...))] attribute on Karn / Pithing Needle /
    // ActionValidator test suites.
}
