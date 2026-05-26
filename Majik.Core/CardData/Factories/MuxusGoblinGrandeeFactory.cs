using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Muxus, Goblin Grandee (Jumpstart, {4}{R}{R}).
///
/// Legendary Creature — Goblin Noble 4/4. Oracle text:
///   "Haste.
///    When Muxus, Goblin Grandee enters, reveal the top six cards of
///    your library. Put all Goblin creature cards from among them onto
///    the battlefield and the rest on the bottom of your library in a
///    random order.
///    Other Goblins you control get +1/+1."
///
/// ## Implemented (v1)
/// - 4/4 Legendary Creature — Goblin Noble at {4}{R}{R}.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/> marker —
///   same wiring as <see cref="GoblinChieftainFactory"/> /
///   <see cref="GoblinGuideFactory"/>.
/// - <b>Lord static (CR 613.1f)</b>: "Other Goblins you control get
///   +1/+1." Wired via <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: Goblin</c>, <c>power: 1, toughness: 1</c>,
///   <c>includeSelf: false</c>, <c>opponentsOnly: false</c>. Same shape
///   as Goblin Chieftain's +1/+1 (minus the granted Haste).
/// - <b>ETB trigger (CR 603.6a)</b>: triggered ability over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> resolving:
///     1. Peek the top 6 cards of the controller's library.
///        Each is published on the supplied <see cref="IEventBus"/> as
///        a <see cref="CardRevealedEvent"/> with reason
///        <c>"muxus"</c> (CR 701.16 — reveal makes them public for
///        the duration of the effect).
///     2. Goblin creature cards (HasType(Creature) AND
///        HasSubtype(Goblin)) are moved Library → Battlefield via
///        <see cref="ZoneService.MoveCard"/> when supplied (so
///        token / ETB / replacement subscribers fire normally —
///        Containment Priest correctly notes Muxus's ETB picks
///        weren't cast); raw zone manipulation otherwise. Owner /
///        controller default to Muxus's controller.
///     3. The non-Goblin / non-creature remainder is bottomed onto
///        the library in a shuffled order via
///        <see cref="GameRandomRegistry.Get"/> (CR 701.20a).
///
/// ## "Goblin creature cards" predicate
/// The printed clause is "Goblin creature cards" — both the Creature
/// type AND the Goblin subtype are required. A pure Goblin tribal
/// instant (none exist in Modern, but the predicate is defensive)
/// would not hit the battlefield. Same gate posture as Atraxa, Grand
/// Unifier's per-type pick: oracle-faithful even when no card today
/// exercises the corner case.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Haste keyword wired;
///   ETB trigger attached but not registered with a
///   <see cref="TriggerManager"/>; lord static not registered (no
///   layers service); battlefield moves use raw zone manipulation.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, ZoneService?, IEventBus?, TriggerManager?)"/>
///   — fully wired. Lord static registers with
///   <paramref name="continuousEffects"/>; ETB trigger registers with
///   <paramref name="triggers"/>; reveals publish on
///   <paramref name="eventBus"/>; battlefield + library moves route
///   through <paramref name="zoneService"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b> on the lord static: <see cref="LordStaticEffect.IsActive"/>
///   short-circuits when Muxus isn't on the battlefield so the +1/+1
///   lifts correctly, but the registration stays on the service.
///   Matches Goblin Chieftain / Plague Engineer posture.
/// - <b>Reveal sticky-public window</b>: <see cref="CardRevealedEvent"/>
///   fires once per peeked card; there's no live tracker for the
///   public-info window (CR 701.16) — clients infer it from the event
///   timestamp. Same posture as every other reveal-from-library
///   factory.
/// - <b>"Onto the battlefield" replacement effects</b>: the move goes
///   through <see cref="ZoneService.MoveCard"/> so the standard
///   replacement / counters / ETB pipeline runs; bespoke "as it
///   enters, choose ..." prompts on the picked Goblins are not driven
///   by Muxus's resolver (the engine's general ETB-choice plumbing
///   handles them when present).
/// </summary>
[CardName("Muxus, Goblin Grandee")]
public static class MuxusGoblinGrandeeFactory
{
    public const string CardName = "Muxus, Goblin Grandee";
    public const string PrintedManaCost = "{4}{R}{R}";
    public const int Power = 4;
    public const int Toughness = 4;
    public const int EtbRevealCount = 6;

    /// <summary>
    /// Construct Muxus with no live runtime wiring. Haste marker is wired;
    /// the ETB trigger and lord static are attached to the card shape but
    /// not registered with any service. Suitable for shape / dispatcher
    /// tests; the ETB effect can still be invoked directly by calling
    /// <see cref="ResolveEtb"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner,
            continuousEffects: null,
            zoneService: null,
            eventBus: null,
            triggers: null);

    /// <summary>
    /// Construct Muxus with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">When supplied, the
    /// "Other Goblins you control get +1/+1" <see cref="LordStaticEffect"/>
    /// is registered against the layers service.</param>
    /// <param name="zoneService">When supplied, ETB-trigger
    /// battlefield placements + library-bottom moves route through
    /// <see cref="ZoneService.MoveCard"/> so <see cref="CardMovedEvent"/>
    /// publishes for ETB-trigger / replacement subscribers (Containment
    /// Priest, Soul Warden, etc.).</param>
    /// <param name="eventBus">When supplied, each of the six peeked cards
    /// publishes a <see cref="CardRevealedEvent"/> with reason
    /// <c>"muxus"</c>.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers
    /// with the bus so a battlefield-entry move automatically queues
    /// the ability on the stack (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
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
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Noble });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — printed Haste. Marker; CombatAbilities.HasHaste reads
        // the KeywordAbility.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 613.1f / CR 613.7c — "Other Goblins you control get +1/+1."
        // includeSelf:false so Muxus doesn't self-pump; opponentsOnly:false
        // (own-controller scope). No granted keywords — pure +1/+1 (the
        // Chieftain shape minus the Haste rider).
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Goblin,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false));
        }

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When Muxus, Goblin Grandee enters, reveal the top six cards
        //    of your library. Put all Goblin creature cards from among
        //    them onto the battlefield and the rest on the bottom of your
        //    library in a random order."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: reveal top {EtbRevealCount}, Goblin creatures → battlefield, " +
            "rest → bottom of library in random order",
            () => ResolveEtb(card.Controller ?? owner, zoneService, eventBus));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Execute Muxus's ETB resolution against <paramref name="controller"/>'s
    /// library + battlefield. Public so tests / bots can invoke the effect
    /// directly without driving the full trigger pipeline. Walks up to
    /// <see cref="EtbRevealCount"/> cards from the top of the library,
    /// publishes a <see cref="CardRevealedEvent"/> per peeked card, moves
    /// Goblin creature cards Library → Battlefield (via
    /// <paramref name="zoneService"/> when supplied), and re-bottoms the
    /// remainder in a shuffled order (CR 701.20a).
    /// </summary>
    public static MuxusEtbResolution ResolveEtb(
        Player controller,
        ZoneService? zoneService = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;
        var peeked = library.GetCards().Take(EtbRevealCount).ToList();
        if (peeked.Count == 0)
        {
            return new MuxusEtbResolution(
                Peeked: Array.Empty<ICard>(),
                ToBattlefield: Array.Empty<ICard>(),
                ToBottom: Array.Empty<ICard>());
        }

        // CR 701.16 — reveal each peeked card. Publish per-card so portal
        // subscribers can flash them.
        foreach (var c in peeked)
        {
            eventBus?.Publish(new CardRevealedEvent(c, controller, ZoneType.Library, "muxus"));
        }

        // Partition. "Goblin creature cards" = HasType(Creature) AND
        // HasSubtype(Goblin) (printed clause is conjunctive).
        var toBattlefield = peeked
            .Where(c => c.HasType(CardType.Creature) && c.HasSubtype(CardSubtype.Goblin))
            .ToList();
        var toBottom = peeked
            .Where(c => !(c.HasType(CardType.Creature) && c.HasSubtype(CardSubtype.Goblin)))
            .ToList();

        // 1) Goblin creatures → battlefield (Library → Battlefield).
        foreach (var goblin in toBattlefield)
        {
            if (zoneService != null)
            {
                // Route through the zone service so ETB triggers,
                // replacement effects (Containment Priest), counter
                // services, etc. all see the move.
                zoneService.MoveCard(
                    goblin,
                    ZoneType.Library,
                    ZoneType.Battlefield,
                    controller);
            }
            else
            {
                library.RemoveCard(goblin);
                controller.Zones.Battlefield.AddCard(goblin);
                goblin.SetController(controller);
                goblin.SetZone(ZoneType.Battlefield);
                if (goblin is Permanent perm) perm.MarkEnteredBattlefield();
            }
        }

        // 2) Remainder → bottom of library in random order.
        // Remove all bottoms from the top first, then append in
        // randomised order so they land underneath any cards that were
        // beneath the original top-6 window.
        foreach (var c in toBottom)
        {
            library.RemoveCard(c);
        }
        var random = GameRandomRegistry.Get(controller);
        random.Shuffle(toBottom);
        foreach (var c in toBottom)
        {
            library.AddCard(c); // Append == bottom.
            c.SetZone(ZoneType.Library);
        }

        return new MuxusEtbResolution(
            Peeked: peeked,
            ToBattlefield: toBattlefield,
            ToBottom: toBottom);
    }

    /// <summary>
    /// Observation record describing one Muxus ETB resolution — the peeked
    /// pile, the Goblin creatures placed on the battlefield, and the cards
    /// bottomed onto the library. Returned by <see cref="ResolveEtb"/> for
    /// tests / bots that want to inspect the resolution without observing
    /// it through the battlefield.
    /// </summary>
    public sealed record MuxusEtbResolution(
        IReadOnlyList<ICard> Peeked,
        IReadOnlyList<ICard> ToBattlefield,
        IReadOnlyList<ICard> ToBottom);
}
