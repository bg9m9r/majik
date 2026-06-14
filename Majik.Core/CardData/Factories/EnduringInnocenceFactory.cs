using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Enduring Innocence (Duskmourn, {1}{W}{W}).
/// Enchantment Creature — Sheep Glimmer 2/1. Oracle text (verified against
/// Scryfall):
///   "Lifelink
///    Whenever one or more other creatures you control with power 2 or less
///    enter, draw a card. This ability triggers only once each turn.
///    When Enduring Innocence dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// Member of the "Enduring" Glimmer cycle (Duskmourn). The base shape (name,
/// Creature + Enchantment types, Sheep + Glimmer subtypes, {1}{W}{W}, 2/1) is
/// materialised from the embedded JSON definition (<c>enduring-innocence.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON declares no abilities —
/// the Lifelink marker, the once-per-turn small-creature ETB draw trigger, and
/// the dies → return-as-enchantment trigger are layered on here (same
/// JSON-backed-identity + code-attached-behaviour posture as
/// <see cref="EnduringCuriosityFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Lifelink (CR 702.15)</b>: a <see cref="KeywordAbility"/> marker (same
///   marker-keyword posture used for other parsed keywords on JSON-backed
///   creatures, e.g. Flash on <see cref="EnduringCuriosityFactory"/>). The
///   damage-dealt life-gain path reads this marker.
///
/// - <b>"Whenever one or more OTHER creatures you control with power 2 or less
///   enter, draw a card. This ability triggers only once each turn."
///   (CR 603.1 / CR 603.6a / CR 603.2c)</b>: a <see cref="TriggeredAbility"/>
///   over <see cref="CardMovedEvent"/> whose predicate matches when the
///   entering card (a) lands on the battlefield, (b) is a Creature, (c) is
///   controlled by THIS card's controller, (d) is NOT this card itself
///   (CR 109.5 — "other"), and (e) has <see cref="Creature.BasePower"/> ≤ 2
///   (printed P/T read; CR 208.2 — same posture as
///   <see cref="GuideOfSoulsFactory"/> / <see cref="MentorOfTheMeekFactory"/>).
///   The "triggers only once each turn" clause is a captured
///   <c>firedThisTurn</c> flag checked inside the predicate: once it has
///   fired this turn the predicate returns false (so no further triggers go on
///   the stack), and the flag is reset to false on every
///   <see cref="TurnStartedEvent"/> (CR 500.1) when an event bus is supplied
///   (same once-per-turn-reset shape proven by
///   <see cref="FaerieMastermindFactory"/>). On resolution the controller
///   draws one card (<see cref="Fx.DrawCards"/>; empty library flags the
///   player for the state-based loss per CR 704.5b).
///
///   Note the printed wording is "one or more": a mass ETB (e.g. several
///   tokens entering simultaneously) is still a single draw. Modelling the
///   batch as one trigger would require a same-event-batch coalescer the
///   engine's per-card <see cref="CardMovedEvent"/> stream doesn't carry; the
///   once-per-turn flag is the dominant constraint and makes the common case
///   correct (the FIRST qualifying ETB this turn draws one card; subsequent
///   ETBs this turn — whether in the same batch or later — do not draw again),
///   which matches the printed "only once each turn" intent. See the deferred
///   note below.
///
/// - <b>Dies → return as an enchantment (CR 603.6c, CR 700.4, CR 701.20,
///   CR 205.2 / 613.1d)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnDies"/> with <c>activeZones = {Battlefield,
///   Graveyard}</c> so the trigger survives the death zone-move. On resolution
///   the card is returned from the graveyard to the battlefield under its
///   owner's control (<see cref="Fx.ReturnFromGraveyardToBattlefield"/>,
///   ZoneService-routed when supplied so ETB triggers fire per CR 603.6a) and a
///   captured <c>hasReturned</c> flag flips true, which gates a
///   <see cref="Layer4TypeStripEffect"/> registered at construction — from that
///   point the Creature type is stripped from the card's layered
///   characteristics ("It's an enchantment. (It's not a creature.)").
///   Identical machinery to <see cref="EnduringCuriosityFactory"/>; see that
///   factory's doc for the layer-vs-printed-type rationale (CR 613.1d).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real Lifelink life-gain</b> is supplied by the damage path reading the
///   Lifelink marker; the factory only attaches the marker (same posture as
///   the other keyword markers on JSON-backed creatures).
/// - <b>"one or more … enter" batch coalescing</b>: each qualifying ETB this
///   turn is evaluated against the once-per-turn flag, so the first one draws
///   and the rest are suppressed. A simultaneous multi-token batch therefore
///   already nets exactly one draw (the first card in the batch trips the flag);
///   the only lossy framing would be an exotic "zero of a batch should still
///   coalesce" case that cannot arise.
/// - <b>Effective vs printed power</b>: reads <see cref="Creature.BasePower"/>
///   (printed P/T) — same posture as Guide of Souls / Mentor of the Meek.
/// </summary>
[CardName("Enduring Innocence")]
public static class EnduringInnocenceFactory
{
    public const string CardName = "Enduring Innocence";
    public const string Slug = "enduring-innocence";

    /// <summary>Maximum (printed) power of an entering creature that triggers the draw.</summary>
    public const int MaxTriggeringPower = 2;

    /// <summary>Cards drawn per once-per-turn small-creature-ETB trigger.</summary>
    public const int DrawCount = 1;

    /// <summary>
    /// Construct Enduring Innocence with no live runtime services. The Lifelink
    /// marker + both triggers are attached for shape inspection. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, continuousEffects: null, zoneService: null);

    /// <summary>
    /// Construct Enduring Innocence with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus. When supplied, a
    /// <see cref="TurnStartedEvent"/> handler resets the once-per-turn
    /// <c>firedThisTurn</c> flag (CR 500.1). May be null.</param>
    /// <param name="triggers">When supplied, both triggered abilities are
    /// registered so the matching events land them on the stack automatically.</param>
    /// <param name="continuousEffects">When supplied, the
    /// <see cref="Layer4TypeStripEffect"/> backing "It's an enchantment.
    /// (It's not a creature.)" is registered on this service (gated OFF until
    /// the card has returned via the dies trigger).</param>
    /// <param name="zoneService">When supplied, the dies trigger's graveyard →
    /// battlefield return routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers fire (CR 603.6a); raw-zone fallback otherwise.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Sheep + Glimmer subtypes, {1}{W}{W}, 2/1). The JSON
        // carries no abilities — Lifelink + the two triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.15 — Lifelink. Marker keyword read by the damage path.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // Captured "this ability has already triggered this turn" flag. Read by
        // the ETB predicate (suppresses further triggers once set) and reset to
        // false on every TurnStartedEvent (CR 500.1) when a bus is supplied.
        var firedThisTurn = false;

        // Captured "the card has returned and is now a non-creature
        // enchantment" flag. Flipped true by the dies trigger after the return;
        // read by both the Layer-4 type-strip predicate and the dies trigger's
        // intervening-if re-check.
        var hasReturned = false;

        // ----------------------------------------------------------------
        // "Whenever one or more OTHER creatures you control with power 2 or
        //  less enter, draw a card. This ability triggers only once each turn."
        //  (CR 603.1 / CR 603.6a / CR 603.2c).
        // Predicate gates on: entering the battlefield, Creature type,
        // controller match, NOT this card (CR 109.5 — "other"), BasePower ≤ 2,
        // and the once-per-turn flag being unset. The flag is set in-effect on
        // resolution so an unresolved trigger leaving the stack doesn't lock
        // the ability for the turn.
        // ----------------------------------------------------------------
        var drawCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (firedThisTurn) return false;
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (!ReferenceEquals(e.Card.Controller, card.Controller ?? owner)) return false;
            if (ReferenceEquals(e.Card, card)) return false; // CR 109.5 — "other"
            if (e.Card is not Creature entering) return false;
            return entering.BasePower <= MaxTriggeringPower;
        });

        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: drawCondition,
            effects: new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: a small creature you control entered — draw {DrawCount} (once each turn)",
                    () =>
                    {
                        // CR 603.2c — "only once each turn": mark fired so the
                        // predicate suppresses any further triggers this turn.
                        // Set even before drawing so a same-turn re-entry can't
                        // re-arm it. Reset on TurnStartedEvent below.
                        firedThisTurn = true;
                        Fx.DrawCards(card.Controller ?? owner, DrawCount);
                    }),
            },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(drawTrigger);
        triggers?.RegisterTriggeredAbility(drawTrigger);

        // CR 500.1 — a new turn re-arms the once-per-turn ability.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => firedThisTurn = false);
        }

        // ----------------------------------------------------------------
        // "When Enduring Innocence dies, if it was a creature, return it to
        //  the battlefield under its owner's control. It's an enchantment.
        //  (It's not a creature.)" (CR 603.6c / CR 700.4 / CR 701.20 /
        //  CR 205.2 / 613.1d). Identical machinery to Enduring Curiosity.
        // ----------------------------------------------------------------
        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: dies — if it was a creature, return it as a (non-creature) enchantment",
                    () => ReturnAsEnchantment(card, zoneService, ref hasReturned)),
            },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // ----------------------------------------------------------------
        // Layer 4 type-strip backing "It's an enchantment. (It's not a
        // creature.)" — CR 205.2 / 613.1d. Registered up-front but gated OFF
        // by the captured hasReturned flag, so the card is a normal creature
        // until the dies trigger returns it. Same machinery / rationale as
        // EnduringCuriosityFactory + HeliodSunCrownedFactory.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            card.ActiveEffects = continuousEffects;
            continuousEffects.Register(new Layer4TypeStripEffect(
                source: card,
                predicate: () => hasReturned));
        }

        return card;
    }

    /// <summary>
    /// Resolve the dies trigger: if the card was a creature when it died,
    /// return it from the graveyard to the battlefield under its owner's
    /// control and flip <paramref name="hasReturned"/> so the Layer-4
    /// type-strip engages. Exposed for direct invocation by tests.
    /// </summary>
    public static void ReturnAsEnchantment(
        Creature card,
        ZoneService? zoneService,
        ref bool hasReturned)
    {
        ArgumentNullException.ThrowIfNull(card);

        // CR 603.6c — intervening "if": only return if it was still a creature
        // when it died. Once it has already returned as a (non-creature)
        // enchantment, a subsequent death fails this check, so it stays put.
        if (hasReturned) return;

        // CR 608.2 — the card must still be in the graveyard at resolution.
        if (card.Zone != ZoneType.Graveyard) return;

        var owner = card.Owner;
        if (owner == null) return;

        // CR 701.20 — graveyard → battlefield under its owner's control.
        Fx.ReturnFromGraveyardToBattlefield(card, owner, zoneService);
        if (card.Zone != ZoneType.Battlefield) return;

        // CR 205.2 / 613.1d — from now on "It's an enchantment. (It's not a
        // creature.)" The Layer4TypeStripEffect registered at construction
        // reads this flag and strips the Creature type on every Compute pass.
        hasReturned = true;
    }
}
