using Majik.Core.Game;

namespace Majik.Core.Combat;

/// <summary>
/// Thread-safe per-game lookup for the live <see cref="AttackRestrictionRegistry"/>
/// — the registry of "creatures can't attack [you] unless their controller
/// pays {cost}" paywalls (CR 508.1g — Ghostly Prison / Propaganda / Sphere of
/// Safety).
///
/// A Ghostly-Prison-class enchantment's ETB effect closure doesn't receive the
/// per-game registry as a parameter (the v1 sync effect model has no service
/// parameter on <see cref="Majik.Core.Abilities.IEffect.Execute"/>), so it
/// looks the registry up here at resolution time and calls
/// <see cref="AttackRestrictionRegistry.Register"/>; when the enchantment
/// leaves the battlefield it <see cref="AttackRestrictionRegistry.Unregister"/>s.
///
/// Mirrors <see cref="Majik.Core.Players.ControlPlayerRegistryProvider"/> /
/// <see cref="Majik.Core.Services.ZoneServiceRegistry"/>: the orchestrator
/// (<see cref="GameDriver"/> / <see cref="Majik.Core.Game"/> facade) installs
/// the per-game store via <see cref="GameRegistryScope.PushForGame"/> and the
/// same instance is handed to <see cref="CombatFlow"/> so the paywall is
/// consulted at declare-attackers. Outside a scope (most unit tests) the
/// ambient store resolves a process-wide fallback, so call sites work
/// unchanged.
/// </summary>
public static class AttackRestrictionRegistryProvider
{
    private static readonly AmbientRegistryStore<AttackRestrictionRegistry> _ambient = new();

    /// <summary>The live per-game registry (the per-game store when a game
    /// scope is installed, otherwise the process-wide fallback).</summary>
    public static AttackRestrictionRegistry Current => _ambient.Current;

    /// <summary>Install a fresh per-game registry. See
    /// <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new AttackRestrictionRegistry());

    /// <summary>Replace the active registry instance (used by the facade /
    /// driver so <see cref="CombatFlow"/> and the factories share ONE
    /// instance).</summary>
    public static IDisposable PushScope(AttackRestrictionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return _ambient.Push(registry);
    }
}
