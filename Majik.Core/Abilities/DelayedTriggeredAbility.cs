using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// Rule 603.7: a one-shot triggered ability created by another effect
/// ("at the beginning of the next end step, sacrifice it"). Auto-unregistered
/// from <see cref="TriggerManager"/> after firing.
///
/// Active in every zone by default — these abilities exist outside the
/// permanent zone restriction of normal triggers (603.7d).
/// </summary>
public sealed class DelayedTriggeredAbility : TriggeredAbility
{
    private static readonly IReadOnlySet<ZoneType> AllZones =
        new HashSet<ZoneType>(Enum.GetValues<ZoneType>());

    public DelayedTriggeredAbility(
        object source,
        Player controller,
        ITriggerCondition condition,
        IEnumerable<ITarget>? targets = null,
        IEnumerable<IEffect>? effects = null,
        Func<bool>? interveningIf = null)
        : base(source, controller, condition, targets, effects, interveningIf,
               activeZones: AllZones)
    {
    }
}
