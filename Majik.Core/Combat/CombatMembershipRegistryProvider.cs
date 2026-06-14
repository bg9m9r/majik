using Majik.Core.Game;

namespace Majik.Core.Combat;

/// <summary>
/// Thread-safe per-game lookup for the live <see cref="CombatMembershipRegistry"/>
/// — the "who is attacking / blocking right now" surface for the current combat
/// (CR 508 / CR 509).
///
/// A target-gated activated ability's candidate gatherer / effect closure
/// doesn't receive the per-game registry as a parameter (the candidate gatherer
/// gets only a <see cref="GameContext"/>; the v1 sync effect model has no
/// service parameter on <see cref="Majik.Core.Abilities.IEffect.Execute"/>), so
/// it looks the registry up here at choice/resolution time and asks
/// <see cref="CombatMembershipRegistry.IsAttackingOrBlocking"/>. The live
/// <see cref="CombatFlow"/> writes the SAME instance (it resolves the registry
/// from this provider) when attackers / blockers are declared and clears it when
/// combat ends.
///
/// Mirrors <see cref="AttackRestrictionRegistryProvider"/> /
/// <see cref="AdditionalCombatRegistryProvider"/>: the orchestrator installs the
/// per-game store via <see cref="GameRegistryScope.PushForGame"/> so every effect
/// closure the game resolves reads THIS game's membership. Outside a scope (most
/// unit tests) the ambient store resolves a process-wide fallback, so call sites
/// work unchanged.
/// </summary>
public static class CombatMembershipRegistryProvider
{
    private static readonly AmbientRegistryStore<CombatMembershipRegistry> _ambient = new();

    /// <summary>The live per-game registry (the per-game store when a game scope
    /// is installed, otherwise the process-wide fallback).</summary>
    public static CombatMembershipRegistry Current => _ambient.Current;

    /// <summary>Install a fresh per-game registry. See
    /// <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new CombatMembershipRegistry());

    /// <summary>Replace the active registry instance (used by the facade /
    /// driver so <see cref="CombatFlow"/> and the factories share ONE
    /// instance).</summary>
    public static IDisposable PushScope(CombatMembershipRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return _ambient.Push(registry);
    }
}
