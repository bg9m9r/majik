using Majik.Core.Cards;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Events;
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

        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
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

    public void Resolve()
    {
        if (_resolutionState.IsResolving)
        {
            throw new InvalidOperationException("Ability is already resolving");
        }

        _resolutionState = ResolutionState.Resolving();

        foreach (var effect in _effects)
        {
            effect.Execute();
        }

        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }
}
