using Majik.Core.Game;

namespace Majik.Core.Players.Agents;

/// <summary>
/// A <see cref="IRebindableGatherer"/> for a "... you control"-scoped
/// <see cref="TargetRequest"/>. Holds the controller whose board the request
/// enumerates plus a pure, controller-parametric <paramref name="select"/>
/// projection (e.g. "creatures on this player's battlefield"). The gatherer
/// reads the controller off this object — NOT a captured closure variable — so
/// re-homing the owning ability onto a new bearer (Agatha's Soul Cauldron,
/// CR 707.2 / 613.1f) only needs to swap the controller via
/// <see cref="RebindController"/>, after which the SAME projection reads the
/// NEW controller's board.
///
/// <para>
/// The <paramref name="select"/> delegate is reused verbatim across rebinds
/// (it takes the controller as its argument, never capturing it), so a
/// re-homed "target creature you control" gathers the bearer-controller's
/// creatures with no behavioural drift.
/// </para>
/// </summary>
public sealed class ControllerScopedGatherer : IRebindableGatherer
{
    private readonly Player _controller;
    private readonly Func<Player, GameContext?, IReadOnlyList<object>> _select;

    /// <summary>
    /// Build a controller-scoped gatherer. <paramref name="select"/> receives
    /// the (current) controller and the live <see cref="GameContext"/> (which
    /// may be null on the context-less sync path, mirroring the existing
    /// <see cref="TargetRequest.CandidateGatherer"/> contract) and returns the
    /// candidate pool. It must be pure and must NOT capture a
    /// <see cref="Player"/> — the controller flows in as the argument so the
    /// gatherer re-homes soundly.
    /// </summary>
    public ControllerScopedGatherer(
        Player controller,
        Func<Player, GameContext?, IReadOnlyList<object>> select)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _select = select ?? throw new ArgumentNullException(nameof(select));
    }

    /// <summary>The controller whose board this gatherer currently scopes to.</summary>
    public Player Controller => _controller;

    /// <inheritdoc />
    public IReadOnlyList<object> Gather(GameContext ctx) => _select(_controller, ctx);

    /// <inheritdoc />
    public IRebindableGatherer RebindController(Player newController) =>
        ReferenceEquals(newController, _controller)
            ? this
            : new ControllerScopedGatherer(newController, _select);
}
