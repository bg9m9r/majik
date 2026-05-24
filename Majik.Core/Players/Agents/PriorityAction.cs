using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;

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

    /// <summary>
    /// Cast a spell. Targets are pre-chosen (engine will validate).
    ///
    /// Optional <paramref name="AlternativeCost"/> elects an alternative
    /// cost (CR 118.9 — flashback, spectacle, evoke, pitch, madness, etc.).
    /// When non-null, the dispatcher forwards it to
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/> which uses the
    /// alt cost's mana cost instead of the printed cost, runs its zone
    /// gate, and fires its post-resolution side-effect.
    ///
    /// Optional <paramref name="AdditionalCosts"/> are CR 601.2f additional
    /// costs (sacrifice/discard riders). Default null on both keeps every
    /// existing call site source-compatible.
    /// </summary>
    public sealed record CastSpell(
        ICard Card,
        IReadOnlyList<object> Targets,
        bool HoldPriority = false,
        IAlternativeCost? AlternativeCost = null,
        IReadOnlyList<IAdditionalCost>? AdditionalCosts = null) : PriorityAction;

    /// <summary>Activate an ability of a permanent the player controls.</summary>
    public sealed record ActivateAbility(IActivatedAbility Ability, IReadOnlyList<object> Targets, bool HoldPriority = false) : PriorityAction;

    /// <summary>
    /// Activate a mana ability (CR 605). Mana abilities don't use the stack
    /// (CR 605.3a) and the activating player retains priority — the
    /// priority loop treats this as an implicit hold-priority so the same
    /// player gets the next prompt without yielding to the opponent.
    /// </summary>
    public sealed record ActivateManaAbility(ICard Source, IManaAbility Ability) : PriorityAction;

    /// <summary>Play a land from hand (special action, doesn't use the stack).</summary>
    public sealed record PlayLand(ICard Land, bool HoldPriority = false) : PriorityAction;

    /// <summary>Shared "pass" singleton — cheap, no allocation per pump.</summary>
    public static readonly PriorityAction Pass = new PassAction();
}
