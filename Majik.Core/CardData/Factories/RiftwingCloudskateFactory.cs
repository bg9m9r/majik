using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Riftwing Cloudskate (Time Spiral, {3}{U}{U}).
///
/// Creature — Illusion 2/2. Oracle text:
///   "Flying
///    When this creature enters, return target permanent to its owner's
///    hand.
///    Suspend 3—{1}{U}"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Illusion, mana cost {3}{U}{U}, owner / controller
///   wired.
/// - <b>Flying</b> keyword marker (CR 702.9) via
///   <see cref="KeywordAbility"/>.
/// - <b>ETB triggered ability</b> (CR 603.6a) fires on the Cloudskate
///   itself entering the battlefield. The trigger declares a single 1..1
///   "target permanent" <see cref="TargetRequest"/> (anyone's permanent
///   — the printed text has no controller filter, mirroring
///   <see cref="VaporSnagFactory"/> / Boomerang / Aether Spellbomb's
///   no-controller-filter bounce). On resolve the targeted permanent is
///   bounced to its owner's hand (CR 701.10) via
///   <see cref="ZoneService.MoveCard"/> when supplied (replacement bus
///   + <see cref="CardMovedEvent"/> publication), otherwise via a raw
///   zone move. CR 608.2b illegal-target re-check: if the target is no
///   longer on the battlefield at resolution, the effect no-ops.
/// - <b>Suspend keyword marker</b>: <see cref="KeywordAbility"/>("Suspend")
///   attached so oracle audits + <see cref="CardData.Parsing.KeywordRegistry"/>
///   consumers detect the keyword without scanning the factory shape.
///   Same posture as Ephemerate's Rebound marker — the actual Suspend
///   mechanic (alt-cost play-from-hand, time-counter delayed trigger,
///   haste-on-resolve cast-from-exile) is a reusable primitive that
///   does not yet exist in the engine.
///
/// ## Deferred (v1 gaps)
/// - <b>Suspend mechanic</b> (CR 702.62): "Rather than cast this card
///   from your hand, you may pay {1}{U} and exile it with three time
///   counters on it. At the beginning of your upkeep, remove a time
///   counter from it. When the last is removed, cast it without paying
///   its mana cost. It has haste." Requires (1) an alternative play-
///   action from hand → exile with N time counters, (2) an exile-
///   resident upkeep delayed trigger that removes counters, (3) a
///   free-cast prompt when the final counter is removed, (4) a haste
///   grant on the resolved-from-exile permanent until it leaves. None
///   of the four halves exist as reusable primitives today. The ETB
///   bounce body is shape-correct without Suspend — when the engine
///   surfaces the "alternative-play-from-hand-to-exile-with-counters"
///   primitive, the marker keyword here becomes the wiring point.
///   Tracked alongside Rebound / Flashback as a shared
///   "alternative-zone-play" primitive backlog item.
/// - <b>Bounce target filter</b>: oracle is "target permanent" (any
///   zone-resident permanent, any controller). v1 candidate gatherer
///   enumerates every battlefield permanent across all players. Bot
///   intent is <see cref="BotIntent.Bounce"/> so the ranker prefers
///   tempo-loss against opponents (same as Reflector Mage / Vapor Snag).
/// </summary>
[CardName("Riftwing Cloudskate")]
public static class RiftwingCloudskateFactory
{
    public const string CardName = "Riftwing Cloudskate";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Riftwing Cloudskate with no live runtime services. ETB
    /// trigger is attached for shape inspection; the bounce uses a raw
    /// zone move when invoked. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Riftwing Cloudskate. When
    /// <paramref name="zoneService"/> is supplied the bounce is routed
    /// through <see cref="ZoneService.MoveCard"/> so the replacement bus
    /// fires and a <see cref="CardMovedEvent"/> is published for
    /// downstream listeners. When <paramref name="triggers"/> is
    /// supplied the ETB <see cref="TriggeredAbility"/> is registered so
    /// the bus drives it on <see cref="CardMovedEvent"/> entry.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Illusion });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.62 — Suspend marker. Mechanic deferred (see class xmldoc
        // "Deferred" gap). Attached so oracle audits / KeywordRegistry
        // detect the keyword without inspecting the factory shape.
        card.AddAbility(new KeywordAbility("Suspend", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.10.
        //   "When this creature enters, return target permanent to its
        //    owner's hand."
        // Target: 1..1 any permanent (no controller filter — bounces
        // friendly permanents too, same as printed Vapor Snag /
        // Boomerang). Resolution-time CR 608.2b legality re-check
        // requires the target still on the battlefield.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: bounce target permanent to its owner's hand (CR 701.10)",
            () =>
            {
                if (etbTrigger == null) return;

                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality re-check.
                if (target.Zone != ZoneType.Battlefield) return;

                var targetOwner = target.Owner;
                if (targetOwner == null) return;

                // CR 701.10 — return to owner's hand.
                if (zoneService != null)
                {
                    // Full path: replacement bus fires, CardMovedEvent
                    // published.
                    zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
                }
                else
                {
                    // Raw fallback: direct zone manipulation (shape
                    // tests / dispatcher path with no ZoneService).
                    var fromController = target.Controller ?? targetOwner;
                    fromController.Zones.Battlefield.RemoveCard(target);
                    targetOwner.Zones.Hand.AddCard(target);
                    target.SetZone(ZoneType.Hand);
                    target.SetController(targetOwner);
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Any battlefield permanent across all players. Bot
                    // ranker scopes to opponents via the Bounce intent.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
