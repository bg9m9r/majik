using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// Shared sacrifice-as-cost rail (CR 601.2f / 701.16) used by the
/// <c>Sacrifice*Cost</c> additional-cost picker family
/// (<see cref="SacrificeCreatureCost"/>, <see cref="SacrificeAnArtifactCost"/>,
/// <see cref="SacrificeFilteredCost"/>, …). Performs the battlefield →
/// owner's-graveyard move every sacrifice cost shares AND, when an
/// <see cref="IEventBus"/> is supplied, publishes a
/// <see cref="PermanentSacrificedEvent"/> crediting the cost-payer as the
/// sacrificing player (CR 701.16a — "its controller") so "whenever a/an
/// [player/opponent] sacrifices …" aristocrat payoffs (It That Betrays,
/// Mayhem Devil, Writhing Chrysalis) fire on the cost-payment path — not just
/// on edict / named-factory sacrifices.
///
/// <para>This consolidates the previously-duplicated raw
/// <c>RemoveCard / AddCard / SetZone</c> triplet each sacrifice cost class
/// inlined (which published nothing). Passing a null bus preserves the legacy
/// publish-nothing posture for unit-test / no-live-game shapes.</para>
/// </summary>
internal static class SacrificeCostHelper
{
    /// <summary>
    /// CR 701.16 — sacrifice <paramref name="permanent"/>: move it from the
    /// paying player's battlefield to its owner's graveyard, then publish a
    /// <see cref="PermanentSacrificedEvent"/> on <paramref name="eventBus"/>
    /// (when non-null) crediting <paramref name="payer"/> as the sacrificing
    /// player (CR 701.16a).
    /// </summary>
    /// <param name="payer">The player paying the cost — the sacrificing player
    /// (CR 601.2f — the cost is paid by the spell/ability's controller, who is
    /// the permanent's controller at sacrifice time, CR 701.16a).</param>
    /// <param name="permanent">The permanent being sacrificed.</param>
    /// <param name="eventBus">Optional event bus. Null preserves the legacy
    /// publish-nothing posture.</param>
    public static void Sacrifice(Player payer, ICard permanent, IEventBus? eventBus)
    {
        if (payer is null) throw new ArgumentNullException(nameof(payer));
        if (permanent is null) throw new ArgumentNullException(nameof(permanent));

        // CR 111.7 — snapshot token-ness BEFORE the move; a token ceases to
        // exist as an SBA the instant it reaches the graveyard, so the flag
        // must be read while it is still the live battlefield object.
        var wasToken = permanent is Permanent p && p.IsToken;

        payer.Zones.Battlefield.RemoveCard(permanent);
        payer.Zones.Graveyard.AddCard(permanent);
        permanent.SetZone(ZoneType.Graveyard);

        // CR 701.16a — publish AFTER the zone move so a payoff that reads the
        // sacrificed card (It That Betrays) finds it in the graveyard.
        eventBus?.Publish(new PermanentSacrificedEvent(permanent, payer, wasToken));
    }
}
