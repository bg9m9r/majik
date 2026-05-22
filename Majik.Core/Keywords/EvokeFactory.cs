using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.74 — Evoke. Produces the printed triggered ability that pairs with
/// the evoke keyword on every evoke creature:
///
///   "When this creature enters, if its evoke cost was paid, sacrifice it."
///
/// Implementation notes:
/// - The trigger fires on the source creature's ETB <see cref="CardMovedEvent"/>
///   (Battlefield destination).
/// - <see cref="TriggeredAbility.InterveningIf"/> re-checks
///   <see cref="Creature.EvokeWasPaid"/> when the ability would go on the stack
///   (CR 603.4). The flag is set by
///   <see cref="Majik.Core.Costs.EvokeAlternativeCost.OnResolved"/> just before
///   the spell's card transitions Stack → Battlefield, so the flag is true
///   here exactly when the spell was cast for its evoke cost.
/// - The sacrifice effect (CR 701.16) moves the creature from battlefield to
///   its owner's graveyard. We guard against the creature having already left
///   the battlefield (rare interaction — Stifle / replacement effects).
/// </summary>
public static class EvokeFactory
{
    public static TriggeredAbility Build(Creature source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, source) && e.ToZone == ZoneType.Battlefield);

        var effect = new Effect("Evoke — sacrifice (CR 702.74b)", () =>
        {
            // Guard: creature must still be on the battlefield to be sacrificed.
            if (source.Zone != ZoneType.Battlefield) return;

            var owner = source.Owner;
            if (owner == null) return;

            // CR 701.16 — sacrifice: move to owner's graveyard.
            owner.Zones.Battlefield.RemoveCard(source);
            owner.Zones.Graveyard.AddCard(source);
            source.SetZone(ZoneType.Graveyard);
        });

        return new TriggeredAbility(
            source,
            source.Controller ?? source.Owner
                ?? throw new InvalidOperationException("Evoke source must have a controller or owner"),
            condition,
            effects: new[] { effect },
            interveningIf: () => source.EvokeWasPaid,
            // Active on Battlefield (where ETB fires); also on Graveyard so the
            // TriggerManager's zone-guard (IsTriggered) doesn't drop the trigger
            // after the sacrifice has moved the card to the graveyard.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }
}
