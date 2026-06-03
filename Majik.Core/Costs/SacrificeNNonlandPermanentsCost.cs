using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice N nonland permanents" — a count-driven activation cost
/// (CR 117 / 701.16). The controller sacrifices exactly
/// <see cref="Count"/> nonland permanents they control. Bolas's Citadel:
/// "{T}, Sacrifice ten nonland permanents: Each opponent loses 10 life."
///
/// Generalizes <see cref="SacrificeFilteredCost"/> (which sacrifices exactly
/// one) to a fixed count. <see cref="CanPay"/> requires the controller to
/// control at least <see cref="Count"/> nonland permanents; <see cref="Pay"/>
/// moves the first <see cref="Count"/> eligible permanents
/// battlefield → owner's graveyard (CR 701.16). v1 picks deterministically
/// (first eligible) — full agent-driven which-permanents prompting is the same
/// deferred MVP the sibling sacrifice-picker costs wait on.
/// </summary>
public sealed class SacrificeNNonlandPermanentsCost : ICost
{
    /// <summary>The number of nonland permanents to sacrifice.</summary>
    public int Count { get; }

    private readonly IEventBus? _eventBus;

    /// <inheritdoc/>
    public string Description => $"sacrifice {Count} nonland permanents";

    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) per sacrifice so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    public SacrificeNNonlandPermanentsCost(int count, IEventBus? eventBus = null)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count),
                "Sacrifice count must be non-negative.");
        Count = count;
        _eventBus = eventBus;
    }

    private static bool IsEligible(Permanent p) =>
        p.Zone == ZoneType.Battlefield && !p.HasType(CardType.Land);

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Count(IsEligible) >= Count;
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!CanPay(player))
            throw new InvalidOperationException(
                $"Cannot pay {Description}: fewer than {Count} nonland permanents controlled.");

        var toSacrifice = player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(IsEligible)
            .Take(Count)
            .ToList();

        foreach (var pick in toSacrifice)
        {
            SacrificeCostHelper.Sacrifice(player, pick, _eventBus);
        }
    }
}
