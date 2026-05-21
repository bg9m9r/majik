using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.93 — Undying: "When this creature dies, if it had no +1/+1
/// counters on it, return it to the battlefield under its owner's control
/// with a +1/+1 counter on it."
///
/// Implementation notes:
/// - The trigger fires on a <see cref="CardMovedEvent"/> from Battlefield to
///   Graveyard where the source is this creature.
/// - <see cref="TriggeredAbility.InterveningIf"/> re-checks the counter
///   condition when the ability would go on the stack (CR 603.4). Since the
///   engine does not clear counters from the <see cref="Permanent.Counters"/>
///   bag when a card leaves the battlefield, the bag's value at the time the
///   trigger resolves accurately reflects what the creature had when it died.
/// - <c>activeZones</c> is set to {Battlefield, Graveyard} so that:
///   (a) SyncCardRegistration keeps the trigger registered after the creature
///       moves to graveyard (necessary because ZoneService sets card.Zone
///       before publishing the CardMovedEvent), and
///   (b) IsTriggered's zone-guard passes when evaluated with the creature in
///       the graveyard.
/// - On return: all counters are cleared (CR 121.2 — counters leave when a
///   permanent leaves the battlefield), then the single +1/+1 counter is
///   added. This ensures the second death after an Undying return correctly
///   suppresses the trigger.
/// </summary>
public static class UndyingFactory
{
    public static TriggeredAbility Build(Creature source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            ReferenceEquals(e.Card, source)
            && e.FromZone == ZoneType.Battlefield
            && e.ToZone == ZoneType.Graveyard);

        var effect = new Effect("Undying — return to battlefield with +1/+1 counter", () =>
        {
            // Guard: creature must still be in graveyard (replacement effects
            // could have moved it, though this is rare).
            if (source.Zone != ZoneType.Graveyard) return;

            var owner = source.Owner;
            if (owner == null) return;

            // Move from graveyard to battlefield (CR 702.93b).
            owner.Zones.Graveyard.RemoveCard(source);
            owner.Zones.Battlefield.AddCard(source);
            source.SetZone(ZoneType.Battlefield);
            source.SetController(owner);

            // CR 121.2 — counters left the battlefield when the creature died.
            // Clear the bag so the second death accurately shows no counters.
            foreach (var entry in source.Counters.All.ToList())
            {
                source.Counters.Remove(entry.Key, entry.Value);
            }

            // Undying grant: one +1/+1 counter (CR 702.93b).
            source.Counters.Add(CounterType.PlusOnePlusOne, 1);

            // Permanent ETB bookkeeping (summoning sickness is already true
            // from construction; re-mark the entry timestamp).
            source.MarkEnteredBattlefield();
        });

        // interveningIf: "if it had no +1/+1 counters on it" (CR 603.4 /
        // CR 702.93). Checked once when the trigger is about to be put on the
        // stack. Counters survive on the graveyard object, so this accurately
        // reflects the state at death.
        return new TriggeredAbility(
            source,
            source.Controller ?? source.Owner
                ?? throw new InvalidOperationException("Undying source must have a controller or owner"),
            condition,
            effects: new[] { effect },
            interveningIf: () => source.Counters.Count(CounterType.PlusOnePlusOne) == 0,
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }
}
