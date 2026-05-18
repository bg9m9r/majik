using Majik.Core.Cards;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Events;
using Majik.Core.Players;
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

    public TriggeredAbility(
        object source,
        Player controller,
        ITriggerCondition condition,
        IEnumerable<ITarget>? targets = null,
        IEnumerable<IEffect>? effects = null,
        Func<bool>? interveningIf = null,
        IEnumerable<ZoneType>? activeZones = null)
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

        if (targets != null)
        {
            _targets.AddRange(targets);
        }

        if (effects != null)
        {
            _effects.AddRange(effects);
        }
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
