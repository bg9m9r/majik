using Majik.Core.Spells;
using Majik.Core.Stack;

namespace Majik.Core.Services;

/// <summary>
/// Primitive for "change the target of a spell" redirection effects
/// (CR 114.6 — "Some effects allow a player to change the target(s) of a
/// spell or ability, and other effects allow a player to choose new targets
/// for a spell or ability.").
///
/// ## Distinct from spell-copy retargeting
/// <see cref="SpellCopier"/> mutates the targets of a fresh COPY it just built
/// (CR 707.10a — "you may choose new targets for the copy"); the original spell
/// is untouched. This redirector mutates the targets of the ORIGINAL spell
/// already on the stack in place — the seam the spell-copy path deliberately
/// did NOT generalize (copy-retarget is closed; original-retarget is this).
///
/// ## What this does
/// <see cref="RedirectSingleTarget"/> rewrites a target spell's lone chosen
/// target (CR 601.2c flat list) to a forced new target. It is built for the
/// "change the target … to this creature" shape (Muck Drubb): the new target is
/// not a player choice, it is fixed by the redirecting effect, so no agent
/// prompt is needed. The spell's effects read <see cref="Spell.ChosenTargets"/>
/// live at <see cref="Spell.ResolveAsync"/> time (a snapshot is taken into the
/// ResolutionContext only when the spell itself begins resolving), so rewriting
/// the slot before the spell resolves redirects its effect to the new target.
///
/// CR 114.6 caveat (legality): a target may only be changed to a legal target.
/// For the forced-self redirection shape the caller has already chosen a legal
/// destination (the redirecting creature itself is a creature, and the
/// redirected spell "targets only a single creature"), so this helper performs
/// the substitution unconditionally — it does NOT re-run the redirected spell's
/// own targeting restrictions, matching the printed Muck Drubb text which forces
/// the new target rather than offering a choice.
/// </summary>
public static class SpellTargetRedirector
{
    /// <summary>
    /// CR 114.6 — change <paramref name="spell"/>'s single chosen target to
    /// <paramref name="newTarget"/>, in place, while it is still on
    /// <paramref name="stack"/>. No-op (returns <c>false</c>) when the spell has
    /// left the stack (CR 608.2b — the redirecting effect's target is no longer
    /// legal) or does not have exactly one chosen target (the redirection shape
    /// only applies to spells that "target only a single" object).
    /// </summary>
    /// <returns><c>true</c> if the target was rewritten; otherwise <c>false</c>.</returns>
    public static bool RedirectSingleTarget(
        Majik.Core.Stack.Stack stack,
        Spell spell,
        object newTarget)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(newTarget);

        // CR 608.2b — the chosen spell must still be on the stack.
        if (!stack.GetAll().Contains(spell)) return false;

        // The redirection shape ("target spell that targets only a single …")
        // requires exactly one chosen target to rewrite.
        if (spell.ChosenTargets.Count != 1) return false;

        spell.ChosenTargets[0] = newTarget;
        return true;
    }
}
