namespace Majik.Core.Game;

/// <summary>
/// Constants shared between the engine's server-side auto-pass
/// (<see cref="PriorityLoop"/>) and the portal's client-side auto-pass
/// (<c>majik.portal/src/app/routes/match/match.ts</c>). Holding these in
/// one place keeps the two implementations in lockstep — a player can
/// reasonably expect identical wait-and-display behaviour whether the
/// server short-circuited the prompt or the portal's own
/// <c>shouldAutoPass</c> intercepted it (the two are belt-and-braces).
/// </summary>
public static class AutoPassConstants
{
    /// <summary>
    /// Minimum-display window (ms) after a stack mutation. While the
    /// timer is active, auto-pass is suppressed even when PassPriority
    /// is the only legal kind — gives the user (and a watching bot) a
    /// beat to register a freshly-landed trigger or spell before it
    /// resolves silently.
    ///
    /// <para>Mirrors <c>STACK_MUTATION_DISPLAY_MS</c> in
    /// <c>majik.portal/src/app/routes/match/match.ts</c>. Bumping this
    /// here without bumping the portal's copy is fine (the portal's
    /// gate still fires on top), but the canonical wait is owned by the
    /// engine so the back-of-house auto-pass volume falls under the
    /// same constant the UX is tuned to.</para>
    /// </summary>
    public const int StackMutationDisplayMs = 600;
}
