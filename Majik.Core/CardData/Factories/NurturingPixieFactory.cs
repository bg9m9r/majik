using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nurturing Pixie (Bloomburrow, {W}).
///
/// Creature — Faerie Rogue 1/1. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, return up to one target non-Faerie,
///    nonland permanent you control to its owner's hand. If a permanent
///    was returned this way, put a +1/+1 counter on this creature."
///
/// The base shape (name, Creature, Faerie + Rogue subtypes, {W}, 1/1) is
/// materialised from the embedded JSON definition (<c>nurturing-pixie.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flying and the ETB
/// bounce-then-grow trigger are layered on here (the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or
/// targeted ETB triggers yet — same posture as
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Faerie Rogue at {W}, owner/controller wired.
/// - <b>Flying (CR 702.9)</b> — <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities"/> surfaces the evasion /
///   block-legality properties (same shape as
///   <see cref="WallOfSwordsFactory"/>).
/// - <b>ETB triggered ability (CR 603.6a)</b> fired by
///   <see cref="CardMovedEvent"/> into <see cref="ZoneType.Battlefield"/>
///   for this card.
///   - TargetRequest declares <b>0..1</b> ("up to one target" — CR 115.1c,
///     so MinTargets = 0 honours the "may choose no target" path).
///   - CandidateGatherer enumerates the controller's battlefield for
///     permanents that are <i>non-Faerie</i> (CR 205.3 subtype check) and
///     <i>nonland</i> (CR 305 — "nonland permanent"). Controller-scoped:
///     "you control" reads <see cref="Permanent.Controller"/> at
///     choose-time (CR 109.5). Nurturing Pixie itself is a Faerie, so it is
///     never a legal candidate.
///   - On resolution: the chosen permanent is returned to its owner's hand
///     (CR 701.10), then — only if a permanent was actually returned — a
///     +1/+1 counter is placed on Nurturing Pixie (CR 122 / CR 121.1: the
///     counter is contingent on the return having happened).
///   - CR 608.2b: an illegal target at resolution (gone from the
///     battlefield, no longer a non-Faerie nonland permanent the Pixie's
///     controller controls) means nothing is returned, so no counter is
///     placed.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + Flying + ETB trigger
///   attached for shape inspection / the <see cref="NamedCardFactory"/>
///   dispatcher; no ZoneService wiring (raw zone-move fallback).
/// - <see cref="Create(Player, ZoneService, TriggerManager)"/> — full
///   wiring: ZoneService routes the bounce (replacement bus + CardMovedEvent
///   fire) and the ETB trigger registers with the TriggerManager so it fires
///   automatically when the Pixie enters.
/// </summary>
[CardName("Nurturing Pixie")]
public static class NurturingPixieFactory
{
    public const string CardName = "Nurturing Pixie";
    public const string Slug = "nurturing-pixie";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Nurturing Pixie with Flying + the ETB trigger attached for
    /// shape inspection. No ZoneService wiring — the bounce uses a raw zone
    /// move. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Nurturing Pixie.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone service for replacement-bus-aware moves.
    /// May be null — a raw zone move is used as fallback.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — trigger is attached to the card shape only.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Faerie + Rogue subtypes, {W}, 1/1). The JSON carries no abilities —
        // Flying + the ETB trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities
        // surfaces evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // --------------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        //   "When this creature enters, return up to one target non-Faerie,
        //    nonland permanent you control to its owner's hand. If a permanent
        //    was returned this way, put a +1/+1 counter on this creature."
        // --------------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName} — return up to one non-Faerie nonland permanent you control to hand, then grow if returned",
            () =>
            {
                if (etbTrigger == null) return;

                // "Up to one target" — zero chosen is legal (CR 115.1c); the
                // return simply doesn't happen and no counter is placed.
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality re-check. The target
                // must still be a non-Faerie, nonland permanent on the
                // battlefield that the Pixie's controller controls.
                var pixieController = card.Controller ?? owner;
                var legal =
                    target.Zone == ZoneType.Battlefield
                    && ReferenceEquals(target.Controller, pixieController)
                    && !target.HasSubtype(CardSubtype.Faerie)
                    && !target.HasType(CardType.Land);
                if (!legal) return;

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

                // "If a permanent was returned this way, put a +1/+1 counter
                // on this creature." The counter is contingent on the return
                // having happened (CR 122 / CR 121.1) — we only reach here
                // when a legal permanent was actually moved to hand. Guard on
                // the Pixie still being on the battlefield (CR 608.2 — an
                // effect acts on the object as it exists at resolution).
                if (card.Zone == ZoneType.Battlefield)
                {
                    Fx.PlaceCounter(card, CounterType.PlusOnePlusOne);
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
                    Description: "up to one target non-Faerie, nonland permanent you control",
                    // "Up to one" — MinTargets = 0 honours the no-target path
                    // (CR 115.1c).
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Controller-scoped gather. CR 109.5 — "you control" reads
                    // Permanent.Controller at choose-time. Exclude Faeries
                    // (CR 205.3) and lands (CR 305 — "nonland permanent").
                    // Nurturing Pixie itself is a Faerie, so it is never a
                    // candidate to return itself.
                    CandidateGatherer: ctx =>
                    {
                        var controller = card.Controller ?? owner;
                        return controller.Zones.Battlefield.GetCards()
                            .OfType<Permanent>()
                            .Where(p => ReferenceEquals(p.Controller, controller)
                                && !p.HasSubtype(CardSubtype.Faerie)
                                && !p.HasType(CardType.Land))
                            .Cast<object>()
                            .ToList();
                    }),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
