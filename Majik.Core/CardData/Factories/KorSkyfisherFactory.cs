using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
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
/// Named-card factory for Kor Skyfisher (Modern Masters / Planar Chaos,
/// Creature — Kor Soldier {1}{W} 2/3).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, return a permanent you control to its
///    owner's hand."
///
/// The base shape (name, Creature, Kor + Soldier subtypes, {1}{W}, 2/3) is
/// materialised from the embedded JSON definition (<c>kor-skyfisher.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (Flying keyword marker, ETB self-bounce trigger) are layered on top here —
/// the JSON <c>AbilityDefinition</c> schema doesn't yet express keyword markers
/// or this bounce shape, so they live in the factory (same posture as the
/// other JSON-backed cards, e.g. <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented
/// - 2/3 Creature — Kor Soldier, mana cost {1}{W}, owner/controller wired.
/// - <b>Flying (CR 702.9)</b> — wired as a <see cref="KeywordAbility"/> marker
///   so <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.CanBlockFlying"/> surface the
///   evasion / block-legality properties (same shape as
///   <see cref="StormscaleScionFactory"/>).
/// - ETB triggered ability (CR 603.6a) fired by <see cref="CardMovedEvent"/>
///   into <see cref="ZoneType.Battlefield"/>.
///   - TargetRequest declares 1..1 "a permanent you control" — unlike
///     <see cref="ReflectorMageFactory"/> (which bounces an OPPONENT's
///     creature), Kor Skyfisher returns one of the CONTROLLER's OWN
///     permanents (self-bounce). The bounce is not optional: "return a
///     permanent you control" (CR 608 — if the controller has any permanent,
///     one must be chosen). The CandidateGatherer enumerates the controller's
///     own battlefield permanents (Kor Skyfisher itself is a legal choice and
///     is typically the only target if it's the controller's sole permanent).
///   - On resolution: the chosen permanent is returned to its owner's hand via
///     <see cref="ZoneService.MoveCard"/> (Battlefield → Hand) when a
///     ZoneService is supplied, or via raw zone move as fallback.
///   - CR 608.2b: if the target is no longer on the battlefield at resolution,
///     the effect does nothing.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + Flying + ETB trigger attached
///   for shape inspection; no ZoneService wiring (raw zone-move fallback).
///   Suitable for shape tests and the <see cref="NamedCardFactory"/> dispatcher.
/// - <see cref="Create(Player, ZoneService, IEventBus, TriggerManager)"/>
///   — full wiring: ZoneService routes the bounce (CR 700.4 / 614 replacement
///   bus fires), eventBus receives CardMovedEvent for downstream triggers, and
///   TriggerManager evaluates the ETB trigger so it fires automatically when the
///   card enters the battlefield. Use this overload from production game setup.
/// </summary>
[CardName("Kor Skyfisher")]
public static class KorSkyfisherFactory
{
    public const string CardName = "Kor Skyfisher";
    public const string Slug = "kor-skyfisher";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Kor Skyfisher with Flying + the ETB trigger attached for shape
    /// inspection. No ZoneService wiring — bounce uses a raw zone move.
    /// Suitable for shape tests and the <see cref="NamedCardFactory"/> dispatcher.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Kor Skyfisher.
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
    /// on bounce. May be null.</param>
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

        // Base shape from the embedded JSON definition (name, Creature, Kor +
        // Soldier subtypes, {1}{W}, 2/3). The JSON carries no abilities —
        // Flying + the ETB bounce are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities surfaces
        // evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // --------------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        // Condition: Kor Skyfisher itself enters the battlefield.
        // --------------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Kor Skyfisher — return a permanent you control to its owner's hand",
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
                    Description: "a permanent you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // CandidateGatherer: any permanent on the CONTROLLER's own
                    // battlefield. Unlike Reflector Mage / Aether Adept the
                    // bounce returns one of YOUR OWN permanents — "a permanent
                    // you control" (CR 109.5 / 608). Kor Skyfisher itself is a
                    // legal choice and is the default when it's your only
                    // permanent.
                    CandidateGatherer: ctx => (ctx.AllPlayers
                            .FirstOrDefault(p => ReferenceEquals(p, card.Controller ?? owner))
                            ?.Zones.Battlefield.GetCards() ?? Enumerable.Empty<Card>())
                        .OfType<Permanent>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);

        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
