using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nahiri, the Harbinger (Shadows over Innistrad,
/// {2}{R}{W}).
///
/// Legendary Planeswalker — Nahiri. Starting loyalty 4.
/// Oracle text (Scryfall, verified):
///   "+2: You may discard a card. If you do, draw a card.
///    −2: Exile target enchantment, tapped artifact, or tapped creature.
///    −8: Search your library for an artifact or creature card, put it onto
///        the battlefield, then shuffle. It gains haste. Return it to your
///        hand at the beginning of the next end step."
///
/// The card's base shape (name, Legendary Planeswalker — Nahiri, {2}{R}{W},
/// loyalty 4) is materialised from the embedded JSON definition
/// (<c>nahiri-the-harbinger.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three loyalty abilities
/// are layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express loyalty abilities, targeted exile, or tutor-to-battlefield, so
/// they live in the factory (same posture as
/// <see cref="UginEyeOfTheStormsFactory"/> /
/// <see cref="TeferiTimeRavelerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>+2: You may discard a card. If you do, draw a card.</b>
///   (CR 606 loyalty + CR 701.16 discard + CR 121 draw.) Uses
///   <see cref="Fx.Discard"/> for the controller's first hand card; the
///   "if you do" rider (CR 700.6 — the draw is conditional on the discard
///   actually happening) gates the <see cref="Fx.DrawCards"/> on a non-empty
///   discard result. Empty hand ⇒ no discard ⇒ no draw (the "may" resolves
///   to declining), loyalty change still applies (CR 606.3).
/// - <b>−2: Exile target enchantment, tapped artifact, or tapped creature.</b>
///   (CR 606 + CR 701.21 exile.) Walks <paramref name="targetResolver"/>'s
///   candidates and exiles the first that satisfies the printed filter
///   (<see cref="IsExileTarget"/>): any enchantment, OR an artifact that is
///   tapped, OR a creature that is tapped (CR 110.5 / CR 701.27 "tapped").
///   No resolver / no legal candidate ⇒ no-op (loyalty change still applies).
/// - <b>−8: Search your library for an artifact or creature card, put it
///   onto the battlefield, then shuffle. It gains haste. Return it to your
///   hand at the beginning of the next end step.</b>
///   (CR 606 + CR 701.19a search + CR 400.7 shuffle + CR 702.10 haste +
///   CR 603.7 delayed trigger.) Deterministic v1 picker: takes the first
///   artifact-or-creature card in the controller's library, moves it
///   Library → Battlefield (via <paramref name="zoneService"/> when supplied
///   so ETB triggers/replacements fire, else raw zone move), shuffles when a
///   <see cref="GameRandom"/> is supplied, grants Haste via a
///   <see cref="KeywordAbility"/> (CR 702.10), and — when a
///   <see cref="TriggerManager"/> is supplied — registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> that returns the permanent to its
///   owner's hand at the beginning of the next end step (CR 603.7, fenced on
///   <c>Timestamp &gt; resolvedAt</c> like Otherworldly Journey).
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompts</b>: <see cref="LoyaltyAbility"/> doesn't declare
///   <see cref="Majik.Core.Targeting.TargetRequest"/>s. −2 picks the first
///   legal candidate from the supplied resolver deterministically; the −8
///   library search auto-takes the first artifact/creature card. Same gap as
///   Karn / Liliana / Ugin.
/// - <b>"You may discard"</b>: +2 auto-discards the first hand card rather
///   than prompting which card (or whether) to discard — same deterministic
///   posture as Liliana of the Veil / Faithless Looting (<see cref="Fx.Discard"/>).
/// - <b>ZoneService routing</b>: the −2 exile and the −8 delayed return use
///   raw zone manipulation on the no-service path, so
///   <see cref="CardMovedEvent"/> isn't published there (same posture as
///   Ugin / Karn). The −8 put-onto-battlefield prefers
///   <see cref="ZoneService.MoveCard"/> when supplied.
/// </summary>
[CardName("Nahiri, the Harbinger")]
public static class NahiriTheHarbingerFactory
{
    public const string CardName = "Nahiri, the Harbinger";
    public const string Slug = "nahiri-the-harbinger";
    public const int StartingLoyalty = 4;
    public const int Plus2Loyalty = +2;
    public const int Minus2Loyalty = -2;
    public const int UltimateLoyalty = -8;

    /// <summary>
    /// Construct Nahiri with no resolvers / services wired — the +2 still
    /// runs (hand / library are owner-scoped), the −2 no-ops (no resolver),
    /// and the −8 puts the first artifact/creature card onto the battlefield
    /// + grants haste but does not shuffle (no random) nor schedule the
    /// delayed return (no trigger manager). Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, targetResolver: null, zoneService: null,
               triggers: null, eventBus: null, random: null);

    /// <summary>
    /// Construct Nahiri, the Harbinger.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetResolver">Returns candidate permanents for the −2
    /// exile. v1 picks the first that satisfies the enchantment / tapped-
    /// artifact / tapped-creature filter. May be null — the clause no-ops.</param>
    /// <param name="zoneService">When supplied, the −8 put-onto-battlefield
    /// + delayed return route through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers / replacements fire. May be null — raw zone moves.</param>
    /// <param name="triggers">When supplied, the −8 registers its delayed
    /// end-step return (CR 603.7). May be null — the put-onto-battlefield
    /// still happens, but the creature/artifact is not returned.</param>
    /// <param name="eventBus">Reserved for parity with sibling planeswalker
    /// factories; currently unused (the delayed return is driven by the
    /// trigger manager, not a bare bus subscription).</param>
    /// <param name="random">Shuffle source for the −8 "then shuffle"
    /// (CR 400.7). May be null — the library is left in order.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Permanent>>? targetResolver,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IEventBus? eventBus,
        GameRandom? random)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = eventBus; // reserved for parity with sibling PW factories

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Nahiri, {2}{R}{W}, loyalty 4). The JSON carries no
        // abilities — the three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var nahiri = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- +2: You may discard a card. If you do, draw a card. -----------
        // CR 606 (loyalty) + CR 701.16 (discard) + CR 121 (draw). The "if you
        // do" rider (CR 700.6) makes the draw conditional on a discard
        // actually occurring; Fx.Discard returns the cards discarded, so an
        // empty hand (zero discarded) skips the draw.
        nahiri.AddAbility(new LoyaltyAbility(nahiri, Plus2Loyalty, () =>
        {
            var controller = nahiri.Controller ?? owner;
            var discarded = Fx.Discard(controller, 1);
            if (discarded.Count > 0)
            {
                Fx.DrawCards(controller, 1);
            }
        }));

        // -- −2: Exile target enchantment, tapped artifact, or tapped
        //    creature. ------------------------------------------------------
        // CR 606 (loyalty) + CR 701.21 (exile). v1 deterministic first-legal
        // pick from the supplied resolver. "Target" — a single permanent.
        nahiri.AddAbility(new LoyaltyAbility(nahiri, Minus2Loyalty, () =>
        {
            var candidates = targetResolver?.Invoke();
            if (candidates == null) return;
            foreach (var p in candidates)
            {
                if (p == null) continue;
                if (p.Zone != ZoneType.Battlefield) continue;
                if (!IsExileTarget(p)) continue;

                // Raw-zone, owner-routed exile (same posture as Ugin / Karn).
                var holder = p.Controller ?? p.Owner;
                holder?.Zones.Battlefield.RemoveCard(p);
                var exileOwner = p.Owner ?? owner;
                exileOwner.Zones.Exile.AddCard(p);
                p.SetZone(ZoneType.Exile);
                return; // "target" — a single permanent.
            }
        }));

        // -- −8: Search your library for an artifact or creature card, put
        //    it onto the battlefield, then shuffle. It gains haste. Return
        //    it to your hand at the beginning of the next end step. ---------
        // CR 606 + CR 701.19a (search) + CR 400.7 (shuffle) + CR 702.10
        // (haste) + CR 603.7 (delayed trigger).
        nahiri.AddAbility(new LoyaltyAbility(nahiri, UltimateLoyalty, () =>
        {
            var controller = nahiri.Controller ?? owner;

            // "an artifact or creature card" — v1 deterministic first pick
            // (CR 701.19a — "may search" auto-taken; agent opt-out deferred).
            var pick = controller.Zones.Library.GetCards()
                .OfType<Permanent>()
                .FirstOrDefault(c => c.HasType(CardType.Artifact)
                                     || c.HasType(CardType.Creature));

            // CR 400.7 — "then shuffle" happens regardless of whether a card
            // was found. Shuffle only when a random source is supplied; a
            // no-op shuffle is rules-immaterial for the observable contract.
            if (pick == null)
            {
                if (random != null) controller.Zones.Library.Shuffle(random);
                return;
            }

            // "put it onto the battlefield" — prefer ZoneService so ETB
            // triggers / replacements fire (CR 603.6a / CR 614); raw move
            // otherwise.
            if (zoneService != null)
            {
                zoneService.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, controller);
            }
            else
            {
                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(controller);
            }

            // "then shuffle" — CR 400.7.
            if (random != null) controller.Zones.Library.Shuffle(random);

            // "It gains haste." — CR 702.10. A static keyword grant on the
            // permanent; same surface as Earthbend's haste grant.
            pick.AddAbility(new KeywordAbility("Haste", pick, controller));

            // "Return it to your hand at the beginning of the next end step."
            // — CR 603.7 delayed triggered ability. Only register when a
            // TriggerManager is supplied (shape-only fallback per
            // Otherworldly Journey).
            if (triggers != null)
            {
                RegisterDelayedReturnToHand(pick, controller, triggers, zoneService);
            }
        }));

        return nahiri;
    }

    /// <summary>
    /// CR 110.5 / CR 701.27 — the −2 exile filter: any enchantment, an
    /// artifact that is tapped, or a creature that is tapped. (A permanent
    /// that is both, e.g. an artifact creature, qualifies if tapped; an
    /// enchantment creature qualifies on the enchantment clause regardless
    /// of tap state.)
    /// </summary>
    private static bool IsExileTarget(Permanent p)
    {
        if (p.HasType(CardType.Enchantment)) return true;
        if (p.HasType(CardType.Artifact) && p.IsTapped) return true;
        if (p.HasType(CardType.Creature) && p.IsTapped) return true;
        return false;
    }

    /// <summary>
    /// CR 603.7 — register a one-shot delayed triggered ability that returns
    /// <paramref name="permanent"/> to its owner's hand at the beginning of
    /// the next end step. Fenced on <c>Timestamp &gt; resolvedAt</c> so it
    /// fires on the FIRST end step after the −8 resolves (same activation-time
    /// fence as Otherworldly Journey).
    /// </summary>
    private static void RegisterDelayedReturnToHand(
        Permanent permanent,
        Player controller,
        TriggerManager triggers,
        ZoneService? zoneService)
    {
        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var returnEffect = Fx.Inline(
            $"{CardName} — return {permanent.Name} to its owner's hand at next end step (CR 603.7)",
            () =>
            {
                // CR 111.8 — guard: only return if it's still the same
                // permanent on the battlefield.
                if (permanent.Zone != ZoneType.Battlefield) return;
                var returnOwner = permanent.Owner ?? controller;

                if (zoneService != null)
                {
                    zoneService.MoveCard(permanent, ZoneType.Battlefield, ZoneType.Hand, returnOwner);
                }
                else
                {
                    var holder = permanent.Controller ?? returnOwner;
                    holder.Zones.Battlefield.RemoveCard(permanent);
                    returnOwner.Zones.Hand.AddCard(permanent);
                    permanent.SetZone(ZoneType.Hand);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: permanent,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End && e.Timestamp > resolvedAt),
            effects: new[] { returnEffect });

        triggers.RegisterDelayed(delayed);
    }
}
