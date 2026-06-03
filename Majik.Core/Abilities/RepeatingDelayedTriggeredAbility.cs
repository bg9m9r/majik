using Majik.Core.Players;
using Majik.Core.Targeting;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 603.7e — a turn-scoped REPEATING delayed triggered ability created by a
/// spell or ability ("until end of turn, whenever X happens, do Y"; e.g. the
/// Beck half of Beck // Call, "Whenever a creature enters this turn, you may
/// draw a card").
///
/// Unlike a one-shot <see cref="DelayedTriggeredAbility"/> — which
/// <see cref="TriggerManager"/> auto-unregisters the first time it fires
/// (CR 603.7) — a repeating delayed trigger STAYS registered after each fire
/// and fires again every time its event recurs. It is torn down only when the
/// turn ends, during the cleanup step (CR 514.2 / CR 603.7e), via
/// <see cref="TriggerManager.ExpireTurnScopedDelayedTriggers"/>.
///
/// Active in every zone by default (inherited from
/// <see cref="DelayedTriggeredAbility"/>) — these abilities have no source on
/// the battlefield and exist outside the permanent-zone restriction of normal
/// triggers (CR 603.7d).
/// </summary>
public sealed class RepeatingDelayedTriggeredAbility : DelayedTriggeredAbility
{
    public RepeatingDelayedTriggeredAbility(
        object source,
        Player controller,
        ITriggerCondition condition,
        IEnumerable<ITarget>? targets = null,
        IEnumerable<IEffect>? effects = null,
        Func<bool>? interveningIf = null)
        : base(source, controller, condition, targets, effects, interveningIf)
    {
    }
}
