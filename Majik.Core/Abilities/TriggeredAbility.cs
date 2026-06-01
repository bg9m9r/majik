using Majik.Core.Cards;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// Represents a triggered ability on the stack.
/// </summary>
public class TriggeredAbility : ITriggeredAbility
{
    private static readonly IReadOnlySet<ZoneType> DefaultActiveZones =
        new HashSet<ZoneType> { ZoneType.Battlefield };

    private ResolutionState _resolutionState;
    private readonly List<ITarget> _targets = new();
    private readonly List<IEffect> _effects = new();

    // Deferred targeting (Rule 601.2c / Rule 603.3): target requests are
    // declared at construction time; chosen targets are stored after the
    // controller's agent responds at stack-entry time.
    private IReadOnlyList<IReadOnlyList<object>> _chosenTargets =
        Array.Empty<IReadOnlyList<object>>();

    public Guid Id { get; }
    public Player Controller { get; }
    public DateTime Timestamp { get; }
    public object Source { get; }
    public IReadOnlyList<ITarget> Targets => _targets.AsReadOnly();
    public IReadOnlyList<IEffect> Effects => _effects.AsReadOnly();
    public bool IsResolving => _resolutionState.IsResolving;
    public ITriggerCondition Condition { get; }
    public Func<bool>? InterveningIf { get; }
    public IReadOnlySet<ZoneType> ActiveZones { get; }

    /// <summary>
    /// Targeting requests that the controller's agent must satisfy when the
    /// ability is about to be placed on the stack (Rule 603.3).
    /// Empty when the ability needs no targets.
    /// </summary>
    public IReadOnlyList<TargetRequest> TargetRequests { get; }

    /// <summary>
    /// The targets chosen by the controller's agent (parallel list-of-lists
    /// matching <see cref="TargetRequests"/>). Populated by
    /// <see cref="SetChosenTargets"/> before the ability is pushed onto the
    /// stack. Effects should read from here, not from the legacy
    /// <see cref="Targets"/> collection.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>> ChosenTargets => _chosenTargets;

    public TriggeredAbility(
        object source,
        Player controller,
        ITriggerCondition condition,
        IEnumerable<ITarget>? targets = null,
        IEnumerable<IEffect>? effects = null,
        Func<bool>? interveningIf = null,
        IEnumerable<ZoneType>? activeZones = null,
        IEnumerable<TargetRequest>? targetRequests = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));

        // PLAN 08 — per-game deterministic id. Reseeded from the ambient
        // DeterministicIdSource inside a game scope; Guid.NewGuid() fallback
        // outside one.
        Id = DeterministicIdScope.NewId();
        // Determinism (PLAN 08 prerequisite): order-determining timestamp comes
        // from the per-game logical clock, not wall-clock. Consumed by
        // ApnapOrdering (.ThenBy(t => t.Timestamp)) + TriggerManager
        // (OrderBy(t => t.Timestamp)). The clock assigns in construction order
        // (the same order UtcNow approximated) so trigger ordering is unchanged
        // but now reproducible on replay.
        Timestamp = LogicalClockScope.Current.NextTimestamp();
        _resolutionState = ResolutionState.NotResolving();
        InterveningIf = interveningIf;
        ActiveZones = activeZones is null
            ? DefaultActiveZones
            : new HashSet<ZoneType>(activeZones);

        TargetRequests = targetRequests is null
            ? Array.Empty<TargetRequest>()
            : targetRequests.ToList().AsReadOnly();

        if (targets != null)
        {
            _targets.AddRange(targets);
        }

        if (effects != null)
        {
            _effects.AddRange(effects);
        }
    }

    /// <summary>
    /// Store the targets chosen by the controller's agent. Called by
    /// <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/> immediately
    /// before pushing the ability onto the stack.
    /// </summary>
    public void SetChosenTargets(IReadOnlyList<IReadOnlyList<object>> chosen)
    {
        _chosenTargets = chosen ?? Array.Empty<IReadOnlyList<object>>();
    }

    public bool IsTriggered(GameEvent e)
    {
        if (e == null)
        {
            return false;
        }

        if (Source is ICard card && !ActiveZones.Contains(card.Zone))
        {
            return false;
        }

        return Condition.Matches(e, this);
    }

    public bool CanBePutOnStack() => InterveningIf?.Invoke() ?? true;

    public void Resolve() => ResolveAsync(null, null).GetAwaiter().GetResult();

    /// <summary>
    /// PLAN 01 — resolve the triggered ability's effects (CR 608) on the
    /// async path. Builds a <see cref="ResolutionContext"/> from this
    /// ability's <see cref="Controller"/> + <see cref="ChosenTargets"/> and
    /// the resolver-supplied <paramref name="agent"/> / <paramref name="game"/>
    /// / <paramref name="ct"/>, then awaits each effect in declaration order.
    /// The synchronous <see cref="Resolve"/> is a thin shim over this.
    /// </summary>
    public async ValueTask ResolveAsync(
        IPlayerAgent? agent,
        GameContext? game,
        CancellationToken ct = default)
    {
        if (_resolutionState.IsResolving)
        {
            throw new InvalidOperationException("Ability is already resolving");
        }

        _resolutionState = ResolutionState.Resolving();

        var rc = ResolutionContext.For(Controller, agent, game, _chosenTargets, ct);

        foreach (var effect in _effects)
        {
            await effect.ExecuteAsync(rc).ConfigureAwait(false);
        }

        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }
}
