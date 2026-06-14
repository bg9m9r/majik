using Majik.Core.Abilities;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
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
/// ## Choosing new targets for the copy (CR 707.10a)
/// <see cref="PushCopyOfTopSpellAsync"/> honours "you may choose new targets for
/// the copy": it reads the original spell's retained per-slot
/// <see cref="TargetRequest"/>s (<see cref="Spell.RetargetRequests"/>, stamped
/// by <see cref="Majik.Core.Game.SpellCastFlow"/> from the resolved
/// <c>SpellDefinition.TargetRequests</c>) and prompts the copier's controller's
/// agent for new targets, held to the same candidate pool / CandidateGatherer
/// the original cast used. A declined slot (empty pick) keeps the original
/// target verbatim, so partial retargeting is supported. The synchronous
/// <see cref="PushCopyOfTopSpell"/> (no agent) keeps the verbatim-reuse
/// behaviour for the Storm / Pyromancer-Ascension family where there is no
/// "may choose new targets" rider.
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

        var copy = BuildCopy(spell, copyController);

        // Push as a new, distinct stack object above the original (CR 706.10a).
        // It is now the top of the stack and resolves first.
        stack.Push(copy);
    }

    /// <summary>
    /// CR 707.10 / 706.10a + CR 707.10a — like <see cref="PushCopyOfTopSpell"/>,
    /// but the copier's controller MAY choose new targets for the copy. If the
    /// original spell carries retained per-slot <see cref="TargetRequest"/>s
    /// (<see cref="Spell.RetargetRequests"/>) and a live
    /// <paramref name="agent"/> + <paramref name="game"/> are supplied, the agent
    /// is prompted per slot ("you may choose new targets for the copy"); a slot
    /// the agent declines (empty pick) keeps the original's target verbatim.
    /// Falls back to verbatim reuse when there are no requests, no agent, or no
    /// game context.
    /// </summary>
    public static async System.Threading.Tasks.ValueTask PushCopyOfTopSpellAsync(
        Majik.Core.Stack.Stack stack,
        IStackObject originalSpell,
        IPlayerAgent? agent,
        GameContext? game,
        Player? copyController = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(originalSpell);

        if (originalSpell is not Spell spell) return;

        var copy = BuildCopy(spell, copyController);

        // CR 707.10a — "you may choose new targets for the copy". Only when the
        // original retained its per-slot requests and we have a live decision
        // surface; otherwise the copy keeps the original's targets verbatim.
        if (agent != null && game != null
            && spell.RetargetRequests is { Count: > 0 } requests)
        {
            await RetargetCopyAsync(copy, requests, agent, game, ct).ConfigureAwait(false);
        }

        stack.Push(copy);
    }

    /// <summary>
    /// Build the distinct copy spell (CR 706.10a). It shares the original's card
    /// (printed characteristics = the copiable snapshot, CR 707.2) and effect
    /// list, is controlled by the copying effect's controller (CR 707.10 —
    /// defaults to the original's controller), is flagged <see cref="Spell.IsCopy"/>
    /// so it ceases to exist on resolution instead of moving the shared card to
    /// a zone (CR 707.10c / 110.5g), and reuses the original's chosen targets
    /// (CR 707.10a — the caller may override them on the retarget path).
    /// </summary>
    private static Spell BuildCopy(Spell spell, Player? copyController)
    {
        var copy = new Spell(
            card: spell.Card,
            controller: copyController ?? spell.Controller,
            effects: spell.Effects)
        {
            IsCopy = true,
            TargetLegalityPredicate = spell.TargetLegalityPredicate,
        };
        foreach (var t in spell.ChosenTargets)
            copy.ChosenTargets.Add(t);
        return copy;
    }

    /// <summary>
    /// CR 707.10a — prompt the copy's controller, per retained request slot, to
    /// choose new targets for the copy. The copy's <see cref="Spell.ChosenTargets"/>
    /// is a flat list (CR 601.2c) aligned slot-for-slot with
    /// <paramref name="requests"/>; a slot whose agent pick is non-empty replaces
    /// the original target(s) for that slot, an empty pick keeps them. New picks
    /// are held to the same candidate pool / CandidateGatherer the original cast
    /// used (the request is unchanged), so a retargeted copy stays legal.
    /// </summary>
    private static async System.Threading.Tasks.ValueTask RetargetCopyAsync(
        Spell copy,
        IReadOnlyList<TargetRequest> requests,
        IPlayerAgent agent,
        GameContext game,
        CancellationToken ct)
    {
        // Rebuild the flat chosen-target list slot by slot. The copy starts with
        // the original's targets in declaration order (one entry per slot for the
        // single-target requests this path serves today); replace per slot.
        var original = copy.ChosenTargets.ToList();
        var rebuilt = new List<object>(original.Count);

        for (var slot = 0; slot < requests.Count; slot++)
        {
            var request = requests[slot];
            var candidates = request.ResolveCandidates(game);
            // Materialize candidates for the prompt (CandidateGatherer ⇒ live pool).
            var promptRequest = candidates == request.LegalCandidates
                ? request
                : request.WithCandidates(candidates);

            var picks = await agent
                .ChooseTargetsAsync(game, promptRequest, ct)
                .ConfigureAwait(false);

            if (picks is { Count: > 0 })
            {
                // New legal targets for this slot (CR 707.10a). Each pick must be
                // among the resolved candidates (legality recheck).
                foreach (var p in picks)
                {
                    if (candidates.Any(c => ReferenceEquals(c, p)))
                        rebuilt.Add(p);
                }
            }
            else if (slot < original.Count)
            {
                // Declined ⇒ keep the original target for this slot verbatim.
                rebuilt.Add(original[slot]);
            }
        }

        // Carry over any trailing original targets the requests didn't cover
        // (defensive: requests should align with the chosen targets).
        for (var i = requests.Count; i < original.Count; i++)
            rebuilt.Add(original[i]);

        copy.ChosenTargets.Clear();
        foreach (var t in rebuilt)
            copy.ChosenTargets.Add(t);
    }
}
