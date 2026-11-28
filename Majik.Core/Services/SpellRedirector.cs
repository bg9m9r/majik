using Majik.Core.Spells;
using Majik.Core.Stack;

namespace Majik.Core.Services;

/// <summary>
/// Primitive for "change the target of target spell with a single target"
/// effects (CR 114.6 — changing targets).
///
/// Binds the Deflection / Imp's Mischief / Shunt / Swerve family.
///
/// ## What this does
///
/// <see cref="Spell.ChosenTargets"/> is the spell's one list of current
/// targets. A spell cast through SpellCastFlow rebuilds its effects from it
/// when it resolves (Spell.SyncEffectsWithTargets), so rewriting it is a real
/// redirect: the effect lands on the new target.
///
///   - Find the most recently pushed <see cref="Spell"/> on the stack
///     whose <see cref="Spell.ChosenTargets"/> count is exactly one.
///   - Replace that single chosen target with <paramref name="newTarget"/>.
///   - Return <c>true</c> if such a spell was found and rewritten,
///     <c>false</c> otherwise.
///
/// Remaining v1 gap: it picks the top single-target spell rather than letting
/// the caster choose which spell (see RedirectTemplate), and it does not check
/// that the new target is legal for that spell (CR 115.7).
/// </summary>
public static class SpellRedirector
{
    /// <summary>
    /// Rewrite the top single-target spell's
    /// <see cref="Spell.ChosenTargets"/> to <paramref name="newTarget"/>.
    /// </summary>
    /// <param name="stack">The shared stack to scan.</param>
    /// <param name="newTarget">Replacement target object.</param>
    /// <returns>
    /// <c>true</c> when an eligible spell was found and its single chosen
    /// target was rewritten; <c>false</c> when no eligible spell exists
    /// (empty stack, no spells, or no spell with exactly one chosen
    /// target — the latter is the common v1 case because
    /// <c>SpellCastFlow</c> doesn't populate <c>ChosenTargets</c> today).
    /// </returns>
    public static bool RedirectTopSpellSingleTarget(
        Majik.Core.Stack.Stack stack,
        object newTarget)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(newTarget);

        if (stack.IsEmpty) return false;

        // Stack.GetAll() is documented as top-to-bottom but in practice
        // returns bottom-to-top (it Reverses the internal LIFO array).
        // Walk the list in reverse so we examine the most recently pushed
        // spell first. Pick the first Spell whose ChosenTargets is exactly
        // one entry.
        var all = stack.GetAll();
        for (var i = all.Count - 1; i >= 0; i--)
        {
            if (all[i] is not Spell spell) continue;
            if (spell.ChosenTargets.Count != 1) continue;
            spell.ChosenTargets[0] = newTarget;
            return true;
        }

        return false;
    }
}
