using Majik.Core.Game;

namespace Majik.Core.Players.Agents;

/// <summary>
/// STAGE 1 analogue to <see cref="Majik.Core.Costs.IRebindableCost"/> for the
/// candidate-gather side of a re-homed activated ability.
///
/// <para>
/// A <see cref="TargetRequest.CandidateGatherer"/> for a controller-scoped
/// target ("target creature YOU control", "another creature you control",
/// "a land you control") enumerates the activating PLAYER's battlefield. When
/// that gatherer is built as a closure capturing a specific
/// <see cref="Player"/> (the authoring card's controller / owner), re-homing
/// the owning <see cref="Majik.Core.Abilities.ActivatedAbility"/> onto a new
/// bearer controlled by a DIFFERENT player — Agatha's Soul Cauldron's grant
/// (CR 707.2 / 613.1f / 702.49) — would leave the gatherer reading the ORIGINAL
/// (exiled) controller's board, so a "you control"-scoped ability would gather
/// the wrong player's permanents.
/// </para>
///
/// <para>
/// A gatherer that implements this interface lets
/// <see cref="Majik.Core.Abilities.ActivatedAbility.RebindTo"/> swap the
/// captured controller for the new bearer's controller the same way
/// <c>IRebindableCost</c> swaps a captured source permanent. Implementations
/// must be pure (return a new gatherer; never mutate the original) so the
/// source ability is unaffected (CR 707.2 — the copy is a separate object).
/// </para>
/// </summary>
public interface IRebindableGatherer
{
    /// <summary>
    /// The live candidate list this gatherer produces for the given context.
    /// Mirrors the plain <see cref="TargetRequest.CandidateGatherer"/>
    /// delegate so the gatherer is usable wherever the delegate is expected.
    /// </summary>
    IReadOnlyList<object> Gather(GameContext ctx);

    /// <summary>
    /// Return an equivalent gatherer scoped to <paramref name="newController"/>
    /// instead of the captured controller. Called by
    /// <see cref="Majik.Core.Abilities.ActivatedAbility.RebindTo"/> when the
    /// owning ability is re-homed onto a bearer controlled by a (possibly)
    /// different player, so a "... you control"-scoped request gathers the NEW
    /// controller's board.
    /// </summary>
    IRebindableGatherer RebindController(Player newController);
}
