using Majik.Core.Abilities;
using Majik.Core.Cards;

namespace Majik.Core.Players.Agents;

/// <summary>
/// What a player chose to do when they had priority. Discriminated union.
/// Returned by <see cref="IPlayerAgent.ChoosePriorityActionAsync"/>.
///
/// <see cref="ActionPriorityAction.HoldPriority"/> (CR 117.3c) — when true,
/// the player retains priority after the action rather than passing it
/// to the next player. Lets a player chain casts/activations (e.g.
/// "tap mana, then immediately cast a spell with it") without giving
/// opponents a window between.
/// </summary>
public abstract record PriorityAction
{
    private PriorityAction() { }

    /// <summary>Pass priority — no action this window.</summary>
    public sealed record PassAction : PriorityAction;

    /// <summary>Cast a spell from hand. Targets are pre-chosen (engine will validate).</summary>
    public sealed record CastSpell(ICard Card, IReadOnlyList<object> Targets, bool HoldPriority = false) : PriorityAction;

    /// <summary>Activate an ability of a permanent the player controls.</summary>
    public sealed record ActivateAbility(IActivatedAbility Ability, IReadOnlyList<object> Targets, bool HoldPriority = false) : PriorityAction;

    /// <summary>Play a land from hand (special action, doesn't use the stack).</summary>
    public sealed record PlayLand(ICard Land, bool HoldPriority = false) : PriorityAction;

    /// <summary>Shared "pass" singleton — cheap, no allocation per pump.</summary>
    public static readonly PriorityAction Pass = new PassAction();
}
