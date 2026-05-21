using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.59 — Earthbend N (Bloomburrow).
///
/// Full rules text:
///   1. Target land you control becomes a 0/0 creature with haste that's
///      still a land.
///   2. Put N +1/+1 counters on it.
///   3. When that land dies or is exiled, return it to the battlefield
///      tapped under its owner's control.
///
/// V1 simplifications / deferred items:
/// - Target selection: auto-picks the first Land the controller controls
///   on the battlefield (no agent prompt).
/// - Base P/T 0/0: the <see cref="Land"/> class has no P/T fields; the
///   engine treats non-Creature cards as 0/0 for combat. With the Creature
///   type added and N +1/+1 counters the effective P/T becomes N/N, which
///   is correct (0 + N = N).
/// - Return-tapped trigger: attached as a <see cref="TriggeredAbility"/>
///   active in {Battlefield, Graveyard, Exile} so it survives zone change
///   (CR 603.6a — same reasoning as UndyingFactory).
/// </summary>
public static class EarthbendAction
{
    /// <summary>
    /// Apply Earthbend N for <paramref name="controller"/>.
    ///
    /// Returns the targeted land, or <c>null</c> if:
    /// - <paramref name="n"/> is &lt;= 0, or
    /// - the controller has no lands on the battlefield.
    /// </summary>
    public static Land? Apply(Player controller, int n)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (n <= 0) return null;

        // Auto-target: first land the controller controls on the battlefield.
        var land = controller.Zones.Battlefield.GetCards()
            .OfType<Land>()
            .FirstOrDefault();
        if (land == null) return null;

        // Step 1a — add Creature type (land retains Land type; CR 701.59a).
        land.AddCardType(CardType.Creature);

        // Step 1b — grant Haste (CR 702.10, CR 701.59a).
        land.AddAbility(new KeywordAbility("Haste", land, controller));

        // Step 2 — put N +1/+1 counters on it (CR 701.59b).
        // With base P/T 0/0 and N counters, effective P/T = N/N (CR 613.3).
        land.Counters.Add(CounterType.PlusOnePlusOne, n);

        // Step 3 — delayed triggered ability: "When [land] dies or is exiled,
        // return it to the battlefield tapped under its owner's control."
        // (CR 701.59c / CR 603.6a)
        //
        // activeZones includes Graveyard and Exile so:
        //   (a) TriggerManager keeps the trigger registered after zone changes, and
        //   (b) IsTriggered's zone-guard passes when the card is no longer on BF.
        var owner = land.Owner ?? controller;

        var returnEffect = new Effect("Earthbend — return to battlefield tapped", () =>
        {
            // Guard: land must actually be in graveyard or exile.
            if (land.Zone == ZoneType.Graveyard)
            {
                owner.Zones.Graveyard.RemoveCard(land);
            }
            else if (land.Zone == ZoneType.Exile)
            {
                owner.Zones.Exile.RemoveCard(land);
            }
            else
            {
                return;
            }

            owner.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            land.SetController(owner);
            land.MarkEnteredBattlefield();
            land.Tap(); // CR 701.59c — returns tapped.
        });

        var returnTrigger = new TriggeredAbility(
            land,
            owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                ReferenceEquals(e.Card, land)
                && e.FromZone == ZoneType.Battlefield
                && (e.ToZone == ZoneType.Graveyard || e.ToZone == ZoneType.Exile)),
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard, ZoneType.Exile });

        land.AddAbility(returnTrigger);

        return land;
    }
}
