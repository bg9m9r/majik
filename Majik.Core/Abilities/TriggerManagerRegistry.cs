using Majik.Core.Game;

namespace Majik.Core.Abilities;

/// <summary>
/// Thread-safe per-game <see cref="TriggerManager"/> lookup. Declarative
/// effect closures that don't receive a <see cref="TriggerManager"/> as a
/// parameter — built by
/// <see cref="Majik.Core.CardData.Definitions.CardDefRuntime.BuildSpellDefinitionFromEffects"/>
/// (the #2128 spell adapter) — look up the live game's manager here so they
/// can register a one-shot <see cref="DelayedTriggeredAbility"/> (CR 603.7) at
/// resolution time.
///
/// <para>
/// This is the trigger-manager analogue of <see cref="Majik.Core.Events.EventBusRegistry"/>
/// / <see cref="Majik.Core.Services.ZoneServiceRegistry"/>. There is exactly
/// one <see cref="TriggerManager"/> per game, so this registry stores a single
/// per-game default rather than a per-player map. The
/// <see cref="GhostlyFlickerFactory"/> / <see cref="OtherworldlyJourneyFactory"/>
/// family of hand-rolled flicker spells thread their <see cref="TriggerManager"/>
/// in explicitly through <c>BuildSpellDefinition(caster, triggers, …)</c>; the
/// declarative <c>exile_with_return</c> verb has no such parameter seam (the
/// JSON adapter builds the resolve closure from the effect def alone), so it
/// reads the live manager from here instead.
/// </para>
///
/// <para>
/// The backing store is <b>not</b> a process-global static. It lives in an
/// <see cref="AmbientRegistryStore{TStore}"/> scoped per-game via
/// <see cref="GameRegistryScope.PushForGame"/> (installed at game start in
/// <c>GameDriver.RunGameAsync</c>, mirroring <see cref="LogicalClockScope"/>),
/// so concurrent matches see independent managers and a finished match's
/// manager is reclaimed when its scope ends. Outside any game scope
/// (direct-construction unit tests) the static API resolves a process-wide
/// fallback store; tests that need a live delayed-return path register their
/// own manager via <see cref="Set"/> and clean up with <see cref="Clear"/>.
/// </para>
/// </summary>
public static class TriggerManagerRegistry
{
    /// <summary>Per-game store: a single trigger manager (one per game).</summary>
    public sealed class Store
    {
        internal readonly object Lock = new();
        internal TriggerManager? Manager;
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>
    /// Install a fresh per-game store as the ambient store for the current
    /// async flow until the returned scope is disposed. Used at game start so
    /// concurrent matches are isolated. See <see cref="GameRegistryScope"/>.
    /// </summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>Register the live game's <see cref="TriggerManager"/>.</summary>
    public static void Set(TriggerManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var s = Current;
        lock (s.Lock) { s.Manager = manager; }
    }

    /// <summary>Return the registered manager, or <see langword="null"/> when
    /// none has been registered (shape-only / pure-construction paths).</summary>
    public static TriggerManager? Get()
    {
        var s = Current;
        lock (s.Lock) return s.Manager;
    }

    /// <summary>Drop the active store's registration (test teardown).</summary>
    public static void Clear()
    {
        var s = Current;
        lock (s.Lock) { s.Manager = null; }
    }
}
