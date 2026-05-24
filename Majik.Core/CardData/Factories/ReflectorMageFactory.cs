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
/// Named-card factory for Reflector Mage (Oath of the Gatewatch,
/// Creature — Human Wizard {1}{W}{U} 2/3).
///
/// Oracle text:
///   "When Reflector Mage enters the battlefield, return target creature an
///    opponent controls to its owner's hand. That creature's owner can't cast
///    spells with the same name as that creature until your next turn."
///
/// ## Implemented (v1)
/// - 2/3 Creature — Human Wizard, mana cost {1}{W}{U}, owner/controller wired.
/// - ETB triggered ability (CR 603.6a) fired by <see cref="CardMovedEvent"/>
///   into <see cref="ZoneType.Battlefield"/>.
///   - TargetRequest declares 1..1 "target creature an opponent controls"
///     (LegalCandidates left for caller — v1 auto-picks the first supplied
///     target in ChosenTargets at resolution time).
///   - On resolution: target creature is bounced to its owner's hand via
///     <see cref="ZoneService.MoveCard"/> (Battlefield → Hand) when a
///     ZoneService is supplied, or via raw zone move as fallback.
///   - CR 608.2b: if the target is no longer on the battlefield at resolution,
///     the effect does nothing.
///
/// ## Deferred (v1 gaps)
/// - "That creature's owner can't cast spells with the same name as that
///   creature until your next turn" — name-based cast restriction needs a
///   delayed-until-next-turn removal surface (no NamedCastRestriction +
///   TurnBoundaryCleanup infrastructure yet). The bounce half ships fully;
///   the name-restriction half is a no-op commented below.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached for
///   shape inspection; no ZoneService wiring (raw zone-move fallback).
///   Suitable for shape tests and the <see cref="NamedCardFactory"/> dispatcher.
/// - <see cref="Create(Player, ZoneService, IEventBus, Majik.Core.Abilities.TriggerManager)"/>
///   — full wiring: ZoneService routes the bounce (CR 700.4 / 614 replacement
///   bus fires), eventBus receives CardMovedEvent for downstream triggers, and
///   TriggerManager evaluates ETB trigger so it fires automatically when the
///   card enters the battlefield. Use this overload from production game setup.
/// </summary>
[CardName("Reflector Mage")]
public static class ReflectorMageFactory
{
    public const string CardName = "Reflector Mage";
    public const string PrintedManaCost = "{1}{W}{U}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Reflector Mage with the ETB trigger attached for shape
    /// inspection. No ZoneService wiring — bounce uses a raw zone move.
    /// Suitable for shape tests and the <see cref="NamedCardFactory"/> dispatcher.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Reflector Mage.
    ///
    /// When <paramref name="zoneService"/> is supplied, the bounce move is
    /// routed through <see cref="ZoneService.MoveCard"/> so the replacement
    /// bus fires and a <see cref="CardMovedEvent"/> is published for downstream
    /// ETB-trigger evaluation. When <paramref name="triggers"/> is supplied,
    /// the ETB TriggeredAbility is registered with the TriggerManager so it
    /// fires automatically via <see cref="CardMovedEvent"/> subscription.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone service for replacement-bus-aware moves.
    /// May be null — raw zone move is used as fallback.</param>
    /// <param name="eventBus">Event bus to publish <see cref="CardMovedEvent"/>
    /// on bounce. May be null — no event published on the raw-move path.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — trigger is attached to the card shape only
    /// (not subscribed for automatic firing).</param>
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        // Condition: Reflector Mage itself enters the battlefield.
        // --------------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Reflector Mage — bounce target creature an opponent controls to its owner's hand",
            () =>
            {
                if (etbTrigger == null) return;

                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Permanent target) return;

                // CR 608.2b — if the target is no longer on the battlefield
                // at resolution, the ability does nothing.
                if (target.Zone != ZoneType.Battlefield) return;

                var targetOwner = target.Owner;
                if (targetOwner == null) return;

                // CR 701.10 — return to owner's hand.
                if (zoneService != null)
                {
                    // Full path: replacement bus fires, CardMovedEvent published.
                    zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
                }
                else
                {
                    // Raw fallback: direct zone manipulation (shape tests /
                    // dispatcher path with no ZoneService).
                    var fromController = target.Controller ?? targetOwner;
                    fromController.Zones.Battlefield.RemoveCard(target);
                    targetOwner.Zones.Hand.AddCard(target);
                    target.SetZone(ZoneType.Hand);
                    target.SetController(targetOwner);
                }

                // DEFERRED: "That creature's owner can't cast spells with the
                // same name as that creature until your next turn." This requires
                // a NamedCastRestriction keyed on the bounced card's name + a
                // delayed-until-next-turn removal trigger. No TurnBoundaryCleanup
                // surface for name-based restrictions exists in v1 — the bounce
                // half ships fully; the name restriction is a no-op.
                //
                // Follow-up: wire a NameCastRestriction effect + DelayedTriggeredAbility
                // over TurnStartedEvent (owner's next turn) to remove it.
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);

        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
