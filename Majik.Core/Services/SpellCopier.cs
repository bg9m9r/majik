using Majik.Core.Spells;
using Majik.Core.Stack;

namespace Majik.Core.Services;

/// <summary>
/// Primitive for "copy a spell" effects (CR 707.10).
///
/// CR 706.10a — A copy of a spell is itself a spell, placed on the stack
/// above the original. CR 707.10 — mana isn't paid for a copy. The
/// controller of the copy may choose new targets (CR 707.10a "you may
/// choose new targets for the copy").
///
/// ## v1 stub — what this actually does
///
/// The engine doesn't yet retain a spell's <c>SpellDefinition</c> /
/// <c>ChosenSpellParams</c> on its <see cref="ISpell"/> after the cast
/// flow (only the constructed <see cref="IEffect"/> list survives). That
/// means we can't reconstruct a fresh copy that re-prompts the agent for
/// targets, nor can we usefully push a second <see cref="Spell"/> wrapping
/// the same <see cref="ICard"/> onto the stack — the second resolution
/// would try to move the (already-resolved) card a second time and the
/// re-resolve guard on <see cref="Spell.Resolve"/> would throw.
///
/// So v1 just re-executes the original spell's effect list in place, at
/// the moment the copy "would have resolved". Lossy semantics:
///   - Copy and original effectively resolve together rather than the copy
///     resolving first on top of the stack (CR 706.10a).
///   - Targets are reused verbatim; the "may choose new targets" rider
///     (CR 707.10a) is dropped.
///   - The copy isn't observable as a distinct <see cref="IStackObject"/>;
///     anything subscribing to <see cref="Majik.Core.Domain.DomainEvents.StackObjectAddedEvent"/>
///     or counting <see cref="Majik.Core.Stack.Stack.Count"/> won't see it.
///
/// Binds the Galvanic Iteration / Doublecast / Howl of the Horde family
/// for now; a real spell-copy stack object is left for follow-up once
/// <c>SpellDefinition</c> + <c>ChosenSpellParams</c> are retained on
/// <see cref="ISpell"/>.
/// </summary>
public static class SpellCopier
{
    /// <summary>
    /// Re-execute the just-cast spell's effects to model a copy of it
    /// (CR 707.10 — lossy v1 stub; see class-level remarks).
    ///
    /// <paramref name="stack"/> is accepted for forward-compat with the
    /// eventual "push a real copy stack object" implementation — v1
    /// ignores it.
    /// </summary>
    /// <param name="stack">
    /// Reserved for the future stack-object implementation. v1 unused.
    /// </param>
    /// <param name="originalSpell">
    /// The spell to copy. Must implement <see cref="ISpell"/>; non-spell
    /// stack objects (activated/triggered abilities) are silently ignored.
    /// </param>
    public static void PushCopyOfTopSpell(
        Majik.Core.Stack.Stack stack,
        IStackObject originalSpell)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(originalSpell);

        // Only spells can be copied here. (CR 707.10 is spell-copy; ability
        // copies route through a different surface.)
        if (originalSpell is not Spell spell) return;

        // Re-run every effect on the original spell. This is the load-bearing
        // semantic the binding cards rely on: "copy that spell" means the
        // listed effects fire a second time. See class-level remarks for the
        // gaps (no stack push, no re-target, simultaneous resolution).
        foreach (var effect in spell.Effects)
        {
            effect.Execute();
        }
    }
}
