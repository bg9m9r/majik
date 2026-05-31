using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.Abilities;

/// <summary>
/// PLAN 01 — the live context handed to an <see cref="IEffect.ExecuteAsync"/>
/// call when a spell / activated ability / triggered ability resolves
/// (CR 608). The stack object constructs it at resolve time from its own
/// <see cref="Controller"/> + chosen targets and the resolver-supplied
/// <see cref="Agent"/> / <see cref="Game"/> / <see cref="Ct"/>.
///
/// <para>
/// <see cref="Agent"/> and <see cref="Game"/> are nullable to support the
/// legacy context-free synchronous execution path (<see cref="IEffect.Execute"/>
/// and effects built from the legacy <c>Effect(string, Action)</c> ctor that
/// capture everything in a closure and never read the context). New async
/// effects that DO need the agent / live game should read them off this
/// record instead of reaching for <see cref="Players.Agents.AgentRegistry"/>
/// or a captured-null <see cref="GameContext"/>.
/// </para>
/// </summary>
public sealed record ResolutionContext(
    Player Controller,
    IPlayerAgent? Agent,
    GameContext? Game,
    IReadOnlyList<IReadOnlyList<object>> ChosenTargets,
    CancellationToken Ct = default)
{
    private static readonly IReadOnlyList<IReadOnlyList<object>> EmptyTargets =
        Array.Empty<IReadOnlyList<object>>();

    /// <summary>
    /// Context for the legacy synchronous execution path — no controller,
    /// agent, game or chosen targets. Used by <see cref="IEffect.Execute"/>
    /// and any caller that re-runs a self-contained sync effect without a
    /// live resolution frame (e.g. spell-copy, nested-effect composition).
    /// Effects built from the legacy <c>Action</c> ctor ignore the context
    /// entirely, so the null fields are never dereferenced on that path.
    /// </summary>
    public static ResolutionContext Legacy { get; } =
        new(Controller: null!, Agent: null, Game: null, ChosenTargets: EmptyTargets);

    /// <summary>
    /// Build a resolution context for a stack object resolving now, defaulting
    /// the chosen-targets list to empty when none were supplied.
    /// </summary>
    public static ResolutionContext For(
        Player controller,
        IPlayerAgent? agent,
        GameContext? game,
        IReadOnlyList<IReadOnlyList<object>>? chosenTargets,
        CancellationToken ct = default)
        => new(controller, agent, game, chosenTargets ?? EmptyTargets, ct);
}
