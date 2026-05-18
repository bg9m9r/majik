using Majik.Core.Abilities;
using Majik.Core.Cards;

namespace Majik.Core.Players.Agents;

/// <summary>
/// What a player chose to do when they had priority. Discriminated union.
/// Returned by <see cref="IPlayerAgent.ChoosePriorityActionAsync"/>.
/// </summary>
public abstract record PriorityAction
{
    private PriorityAction() { }

    /// <summary>Pass priority — no action this window.</summary>
    public sealed record PassAction : PriorityAction;

    /// <summary>Cast a spell from hand. Targets are pre-chosen (engine will validate).</summary>
    public sealed record CastSpell(ICard Card, IReadOnlyList<object> Targets) : PriorityAction;

    /// <summary>Activate an ability of a permanent the player controls.</summary>
    public sealed record ActivateAbility(IActivatedAbility Ability, IReadOnlyList<object> Targets) : PriorityAction;

    /// <summary>Play a land from hand (special action, doesn't use the stack).</summary>
    public sealed record PlayLand(ICard Land) : PriorityAction;

    /// <summary>Shared "pass" singleton — cheap, no allocation per pump.</summary>
    public static readonly PriorityAction Pass = new PassAction();
}
