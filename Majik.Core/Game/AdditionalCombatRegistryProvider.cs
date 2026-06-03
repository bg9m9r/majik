namespace Majik.Core.Game;

/// <summary>
/// Thread-safe per-game lookup for the live <see cref="AdditionalCombatQueue"/>
/// — the queue of "there is an additional combat phase" grants (CR 506.4 —
/// Aggravated Assault, Combat Celebrant, Fear of Missing Out's Delirium clause).
///
/// A card's triggered-ability effect closure doesn't receive the
/// <see cref="TurnDriver"/>'s per-turn queue as a parameter (the v1 sync effect
/// model has no service parameter on
/// <see cref="Majik.Core.Abilities.IEffect.Execute"/>), so it looks the queue up
/// here at resolution time and calls
/// <see cref="AdditionalCombatQueue.EnqueueAdditional"/>. The turn loop in
/// <see cref="TurnDriver"/> reads the SAME instance (it resolves its queue from
/// this provider) and re-enters combat for each pending grant after the current
/// combat finishes (CR 506.4).
///
/// Mirrors <see cref="Majik.Core.Combat.AttackRestrictionRegistryProvider"/> /
/// <see cref="Majik.Core.Players.ControlPlayerRegistryProvider"/>: the
/// orchestrator installs the per-game store via
/// <see cref="GameRegistryScope.PushForGame"/> so every effect closure the game
/// resolves reads THIS game's queue. Outside a scope (most unit tests) the
/// ambient store resolves a process-wide fallback, so call sites work unchanged.
/// </summary>
public static class AdditionalCombatRegistryProvider
{
    private static readonly AmbientRegistryStore<AdditionalCombatQueue> _ambient = new();

    /// <summary>The live per-game queue (the per-game store when a game scope
    /// is installed, otherwise the process-wide fallback).</summary>
    public static AdditionalCombatQueue Current => _ambient.Current;

    /// <summary>Install a fresh per-game queue. See
    /// <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new AdditionalCombatQueue());

    /// <summary>Replace the active queue instance (used by the facade / driver
    /// so <see cref="TurnDriver"/> and the factories share ONE instance).</summary>
    public static IDisposable PushScope(AdditionalCombatQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return _ambient.Push(queue);
    }
}
