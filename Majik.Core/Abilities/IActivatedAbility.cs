using Majik.Core.Costs;
using Majik.Core.Stack;
using Majik.Core.Targeting;

namespace Majik.Core.Abilities;

/// <summary>
/// Interface for activated abilities on the stack.
/// </summary>
public interface IActivatedAbility : IStackObject, IAbility
{
    /// <summary>
    /// The source of this ability (card or permanent).
    /// </summary>
    object Source { get; }

    /// <summary>
    /// The targets chosen for this ability (if any).
    /// </summary>
    IReadOnlyList<ITarget> Targets { get; }

    /// <summary>
    /// The costs paid for this ability.
    /// </summary>
    IReadOnlyList<ICost> Costs { get; }

    /// <summary>
    /// True when this activated ability carries an "Activate only as a
    /// sorcery" rider (CR 117.1a / 307.5). The action validator gates
    /// activations on this flag — the ability is only legal during the
    /// controller's main phase with an empty stack. Default false
    /// (any-time activation, subject only to the usual instant-speed
    /// timing rules).
    /// </summary>
    bool IsSorcerySpeed { get; }

    /// <summary>
    /// CR 602.5c / 605.1a — true iff this ability's "Activate only if
    /// &lt;condition&gt;" gate (if any) is currently satisfied. Always true
    /// for abilities without such a restriction. Re-evaluated against live
    /// game state on each call. The <see cref="Rules.ActionValidator"/> and
    /// <see cref="Services.AbilityActivator"/> consult this before allowing
    /// an activation.
    /// </summary>
    bool CanActivateNow();
}
