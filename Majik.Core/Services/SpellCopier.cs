using Majik.Core.Abilities;
using Majik.Core.Players;
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
/// ## What this does (distinct copy stack object)
///
/// <see cref="PushCopyOfTopSpell"/> constructs a distinct, independent copy
/// <see cref="Spell"/> from an existing spell and <see cref="Majik.Core.Stack.Stack.Push">
/// pushes</see> it onto the stack as a new <see cref="IStackObject"/>
/// (CR 706.10a — placed above the original). The copy:
///   - is its own <see cref="IStackObject"/> with its own <see cref="Spell.Id"/>
///     (observable via <see cref="Majik.Core.Domain.DomainEvents.StackObjectAddedEvent"/>
///     and <see cref="Majik.Core.Stack.Stack.Count"/>);
///   - snapshots the original's copiable characteristics — it shares the
///     original's <see cref="ICard"/> (the card's printed characteristics are
///     the snapshot, CR 707.2) plus the same effect list, and is controlled by
///     the original's controller (CR 707.10);
///   - reuses the original's chosen targets verbatim (CR 707.10a — the
///     "you may choose new targets" rider is the residual deferral; see below);
///   - resolves FIRST (LIFO, on top), then CEASES TO EXIST without moving any
///     card to a zone (CR 707.10c / CR 110.5g — a copy on the stack ceases to
///     exist as a state-based action). <see cref="Majik.Core.Services.StackResolver"/>
///     reads <see cref="Spell.IsCopy"/> to skip the post-resolution zone move,
///     so the original card is left exactly where it was.
///
/// Binds the Galvanic Iteration / Doublecast / Howl of the Horde / Storm /
/// Pyromancer Ascension family. Snapcaster Mage / Bloodthirsty Adversary use
/// the separate cast-from-graveyard path (CR 702.34 flashback grant), not this
/// copier — they cast the real card, they don't copy a spell.
///
/// ## Residual deferral
/// - <b>"You may choose new targets for the copy"</b> (CR 707.10a): the copy
///   reuses the original's chosen targets verbatim. Re-prompting the controller
///   for new targets needs the original spell's per-target TargetRequest +
///   CandidateGatherer retained on <see cref="ISpell"/> after the cast flow
///   (only the constructed <see cref="IEffect"/> list survives today), plus a
///   live agent at copy-creation time. Left for follow-up.
/// </summary>
public static class SpellCopier
{
    /// <summary>
    /// Construct a distinct copy of <paramref name="originalSpell"/> and push
    /// it onto <paramref name="stack"/> as a new <see cref="IStackObject"/>
    /// above the original (CR 706.10a / 707.10). The copy resolves first and
    /// then ceases to exist (CR 707.10c) — see class-level remarks.
    /// </summary>
    /// <param name="stack">The stack to push the copy onto (above the original).</param>
    /// <param name="originalSpell">
    /// The spell to copy. Must implement <see cref="ISpell"/>; non-spell
    /// stack objects (activated/triggered abilities) are silently ignored.
    /// </param>
    /// <param name="copyController">
    /// CR 707.10 — the controller of the copy is the player who controls the
    /// effect that created it. For Storm / Galvanic Iteration / Pyromancer
    /// Ascension that player IS the original spell's controller, so this
    /// defaults to <c>null</c> ⇒ the original's controller. For "copy target
    /// spell" effects (Twincast / Reverberate) the copier and the targeted
    /// spell's controller can differ — the caster passes themselves here.
    /// </param>
    public static void PushCopyOfTopSpell(
        Majik.Core.Stack.Stack stack,
        IStackObject originalSpell,
        Player? copyController = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(originalSpell);

        // Only spells can be copied here. (CR 707.10 is spell-copy; ability
        // copies route through a different surface.)
        if (originalSpell is not Spell spell) return;

        // Build a distinct copy spell (CR 706.10a). It shares the original's
        // card (printed characteristics = the copiable snapshot, CR 707.2) and
        // effect list, is controlled by the copying effect's controller
        // (CR 707.10 — defaults to the original's controller), and is flagged
        // IsCopy so it ceases to exist on resolution instead of moving the
        // shared card to a zone (CR 707.10c / 110.5g).
        var copy = new Spell(
            card: spell.Card,
            controller: copyController ?? spell.Controller,
            effects: spell.Effects)
        {
            IsCopy = true,
            // CR 707.10a — the copy reuses the original's chosen targets
            // verbatim (the "may choose new targets" rider is the residual
            // deferral). The flat cast-time list (CR 601.2c) carries over so
            // resolution reads ChosenTargets[0] just like the original.
            TargetLegalityPredicate = spell.TargetLegalityPredicate,
        };
        foreach (var t in spell.ChosenTargets)
            copy.ChosenTargets.Add(t);

        // Push as a new, distinct stack object above the original (CR 706.10a).
        // It is now the top of the stack and resolves first.
        stack.Push(copy);
    }
}
