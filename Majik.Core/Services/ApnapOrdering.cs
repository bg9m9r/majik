using Majik.Core.Abilities;
using Majik.Core.Players;

namespace Majik.Core.Services;

/// <summary>
/// APNAP (Active Player, Non-Active Player) ordering of pending triggered
/// abilities (Rule 603.3b). The active player's triggers are placed on the
/// stack first; within a player, controller chooses order (we default to
/// deterministic timestamp ascending — earliest fired first).
/// </summary>
public static class ApnapOrdering
{
    public static IReadOnlyList<ITriggeredAbility> Order(
        IEnumerable<ITriggeredAbility> triggers,
        Player activePlayer)
    {
        if (triggers == null)
        {
            throw new ArgumentNullException(nameof(triggers));
        }

        if (activePlayer == null)
        {
            throw new ArgumentNullException(nameof(activePlayer));
        }

        return triggers
            .OrderBy(t => ReferenceEquals(t.Controller, activePlayer) ? 0 : 1)
            .ThenBy(t => t.Timestamp)
            .ToList();
    }
}
