namespace Majik.Core.Game;

/// <summary>
/// CR 506.4 / CR 505.1b — additional combat phases (and the optional
/// additional main phase that follows some of them). Effects like Combat
/// Celebrant or Fear of Missing Out's Delirium clause push extra combats
/// onto this queue during a turn; effects like Relentless Assault or World
/// at War push an extra combat that is "followed by an additional main
/// phase." <see cref="TurnDriver"/> consults <see cref="HasAdditional"/>
/// after the current combat finishes and re-enters the combat sequence
/// (plus a postcombat main, when the consumed grant requested one) for each
/// pending grant.
///
/// CR 500.7 — extra phases are taken in the order they were created (the
/// turn-based-action that introduces them processes them as a unit), so the
/// per-grant flags are consumed FIFO.
/// </summary>
public sealed class AdditionalCombatQueue
{
    // FIFO of pending grants. Each entry's bool = "followed by an additional
    // main phase" (CR 505.1b). Combat-only grants (Combat Celebrant, Fear of
    // Missing Out) enqueue false; combat-then-main grants (Relentless Assault,
    // World at War) enqueue true.
    private readonly Queue<bool> _pending = new();

    public int Pending => _pending.Count;
    public bool HasAdditional => _pending.Count > 0;

    /// <summary>Enqueue an additional combat phase. When
    /// <paramref name="followedByMainPhase"/> is true the extra combat is
    /// followed by an additional (postcombat) main phase (CR 505.1b —
    /// Relentless Assault / World at War). Defaults to combat-only
    /// (CR 506.4 — Combat Celebrant / Fear of Missing Out).</summary>
    public void EnqueueAdditional(bool followedByMainPhase = false)
        => _pending.Enqueue(followedByMainPhase);

    /// <summary>Dequeue the next pending grant (count-only consumer). Combat-only
    /// vs combat-then-main is exposed via the
    /// <see cref="TryConsume(out bool)"/> overload.</summary>
    public bool TryConsume() => TryConsume(out _);

    /// <summary>Dequeue the next pending grant, reporting whether it is
    /// "followed by an additional main phase" (CR 505.1b).</summary>
    public bool TryConsume(out bool followedByMainPhase)
    {
        if (_pending.Count == 0) { followedByMainPhase = false; return false; }
        followedByMainPhase = _pending.Dequeue();
        return true;
    }

    public void Reset() => _pending.Clear();
}
