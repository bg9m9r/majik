namespace Majik.Core.Game;

/// <summary>
/// Constant documenting the minimum-display window the portal enforces
/// client-side after a stack mutation, kept here so the engine's UX-tuning
/// reference lives next to the auto-pass code it pairs with.
/// </summary>
public static class AutoPassConstants
{
    /// <summary>
    /// Minimum-display window (ms) after a stack mutation, enforced
    /// CLIENT-SIDE only (the portal's <c>STACK_MUTATION_DISPLAY_MS</c> in
    /// <c>majik.portal/src/app/routes/match/match.ts</c>): the portal holds
    /// a freshly-landed trigger or spell on screen for a beat before its
    /// auto-pass fires, so it doesn't resolve silently the instant it lands.
    ///
    /// <para>The engine deliberately does NOT enforce this beat. A prior
    /// server-side "stack-display" gate in <see cref="PriorityLoop"/>
    /// suppressed auto-pass for this window; because own-top is exempt, it
    /// only ever fired on the dead (pass-only), not-own-top case — exactly
    /// the window where blocking on a human whose only legal move is pass
    /// wedged a live match permanently (replay-confirmed). The beat is
    /// purely cosmetic and belongs on the client, where it cannot deadlock
    /// the engine; this constant remains as the value the portal is tuned
    /// to and as documentation of why the server enforces none.</para>
    /// </summary>
    public const int StackMutationDisplayMs = 600;
}
