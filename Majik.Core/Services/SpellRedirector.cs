using Majik.Core.Spells;
using Majik.Core.Stack;

namespace Majik.Core.Services;

/// <summary>
/// Primitive for "change the target of target spell with a single target"
/// effects (CR 114.6 — changing targets).
///
/// Binds the Deflection / Imp's Mischief / Shunt / Swerve family.
///
/// ## v1 stub — what this actually does
///
/// The engine doesn't retain a spell's <c>SpellDefinition</c> /
/// <c>ChosenSpellParams</c> on its <see cref="Majik.Core.Spells.ISpell"/>
/// after the cast flow. The pre-built <see cref="Majik.Core.Abilities.IEffect"/>
/// closures captured their targets via the cast-time
/// <c>resolver(p.Targets[0][0])</c> call — those captures are baked into
/// the closure and the redirector cannot rewrite them post-cast.
///
/// What the v1 helper does instead:
///   - Find the most recently pushed <see cref="Spell"/> on the stack
///     whose <see cref="Spell.ChosenTargets"/> count is exactly one.
///   - Replace that single chosen target with <paramref name="newTarget"/>.
///   - Return <c>true</c> if such a spell was found and rewritten,
///     <c>false</c> otherwise.
///
/// Observable effect (v1): the spell's <c>ChosenTargets</c> reflects the
/// new pick (visible to <see cref="Majik.Core.Services.StackResolver"/>'s
/// CR 608.2b legality recheck), but the actual effect closure still
/// resolves against the original target. This is a clear lossy stub —
/// callers / cards bind, the engine acknowledges the redirect, but the
/// damage / counter / destroy ultimately lands on the original creature.
///
/// Better than no-bind: the card now flows through the cast pipeline,
/// the agent gets to pick a "new target", and downstream wiring (resolver
/// + effect-rebuild on stack) can flip the v1 stub into real semantics
/// without touching the binding templates.
/// </summary>
public static class SpellRedirector
{
    /// <summary>
    /// v1 stub: rewrite the top single-target spell's
    /// <see cref="Spell.ChosenTargets"/> to <paramref name="newTarget"/>.
    /// See class-level remarks for the lossy semantics.
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
