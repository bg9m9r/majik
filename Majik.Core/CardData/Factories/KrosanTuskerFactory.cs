using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Krosan Tusker (Onslaught, {5}{G}{G}).
///
/// Creature — Beast 6/5. Oracle text (Scryfall):
///   "Cycling {2} ({2}, Discard this card: Draw a card.)
///    When you cycle Krosan Tusker, you may search your library for a
///    basic land card, reveal it, put it into your hand, then shuffle."
///
/// ## Implemented (v1)
/// - <b>Creature — Beast {5}{G}{G} 6/5</b>. Big-stupid-Onslaught beater
///   designed to be cycled for a basic land far more often than cast.
/// - <b>Cycling {2}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{2}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers <see cref="DiscardSelfCost"/> (CR 702.32a
///   hand-zone gate) on the cost stack, and on resolve draws a card
///   then publishes <see cref="CardCycledEvent"/> for CR 702.32d
///   subscribers (Lightning Rift, Astral Slide, etc.). NB: Krosan
///   Tusker's printed cycle is generic Cycling — the basic-land tutor
///   is a SEPARATE on-cycle triggered ability per CR 702.32d's "When
///   you cycle this card" rider, not a typed-cycling variant.
/// - <b>"When you cycle Krosan Tusker, you may search your library for
///   a basic land card …" trigger</b> (CR 702.32d / CR 603.6): wired
///   as a <see cref="TriggeredAbility"/> over
///   <see cref="EventTriggerCondition{CardCycledEvent}"/> gated to
///   <c>ReferenceEquals(e.Card, card)</c> (printed self-cycle gate,
///   distinct from Curator of Mysteries' "another card" gate; same
///   posture as Decree of Pain's self-cycle rider). The trigger lives
///   in <see cref="ZoneType.Graveyard"/> because the cycling resolve
///   body has already routed Krosan Tusker hand → graveyard by the
///   time the event publishes (CR 702.32a — discard self happens
///   before the post-resolve event publish). Resolution invokes
///   <see cref="TypedCyclingFactory.TutorTypedCard"/> with predicate
///   <c>c =&gt; c.HasType(CardType.Land) &amp;&amp; c.HasSupertype(CardSupertype.Basic)</c>
///   for the "basic land card" tutor — same primitive Forestcycling
///   uses (CR 701.19a agent prompt + deterministic first-match
///   fallback + CR 701.20a shuffle). The "you may" optional rider is
///   honored end-to-end: an agent returning null = decline (CR 701.19a
///   "search is an action a player may decline"), but the v1
///   shape-only path with no agent registered defaults to find-and-keep
///   to preserve the deck-fix tempo line (same posture as every other
///   tutor primitive in the engine — Stoneforge Mystic, Expedition
///   Map, Sylvan Scrying).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Both the on-cycle
///   trigger and the cycling activated ability are attached for shape
///   inspection; cycling has no event bus (shape-only — no
///   <see cref="CardCycledEvent"/> publication, so the on-cycle trigger
///   will not fire automatically without one).
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully
///   wired. The on-cycle trigger is registered so
///   <see cref="CardCycledEvent"/> publications auto-queue it; cycling
///   resolve publishes against the supplied bus so the trigger fires
///   end-to-end.
///
/// CR rule references: 205.3m (Beast subtype), 205.4a (Basic
/// supertype), 603.6 (triggered ability), 701.19a (library search),
/// 701.20a (shuffle), 702.32 (Cycling), 702.32d ("When you cycle"
/// trigger).
/// </summary>
[CardName("Krosan Tusker")]
public static class KrosanTuskerFactory
{
    public const string CardName = "Krosan Tusker";
    public const string PrintedManaCost = "{5}{G}{G}";
    public const int Power = 6;
    public const int Toughness = 5;
    public const string CyclingCost = "{2}";

    /// <summary>
    /// Construct Krosan Tusker with no live wiring. On-cycle trigger
    /// attached for shape inspection; cycling ability attached without
    /// an event bus (no <see cref="CardCycledEvent"/> publication).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Krosan Tusker. When <paramref name="triggers"/> is
    /// supplied the on-cycle trigger is registered so a self-cycle
    /// <see cref="CardCycledEvent"/> queues the basic-land tutor. When
    /// <paramref name="eventBus"/> is supplied the cycling resolve body
    /// publishes <see cref="CardCycledEvent"/> against the bus so the
    /// trigger fires automatically end-to-end.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cycling {2} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a), draws a card
        // (CR 702.32a "Draw a card"), and publishes CardCycledEvent
        // (CR 702.32d) for the on-cycle rider below + any
        // CR 702.32d subscribers (Lightning Rift, Astral Slide, etc.).
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        // ----------------------------------------------------------------
        // "When you cycle Krosan Tusker, you may search your library for
        // a basic land card, reveal it, put it into your hand, then
        // shuffle." (CR 702.32d / CR 603.6)
        //
        // EventTriggerCondition<CardCycledEvent> gated to:
        //   1. ReferenceEquals(e.Card, card) — printed self-cycle gate.
        //   2. ReferenceEquals(e.Player, owner) — defense-in-depth
        //      ("you" = the cycling player; controller == owner since
        //      the card is being cycled from owner's hand).
        // ActiveZones = {Graveyard} because the cycling resolve body
        // has already routed Krosan Tusker hand → graveyard before the
        // event publishes (CR 702.32a — discard self happens first).
        // Same posture as Decree of Pain.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: tutor a basic land -> hand + shuffle",
            () =>
            {
                TypedCyclingFactory.TutorTypedCard(
                    owner: owner,
                    predicate: c =>
                        c.HasType(CardType.Land)
                        && c.HasSupertype(CardSupertype.Basic),
                    kindLabel: "basic land card",
                    shuffleReason: "krosan-tusker-cycle");
            });

        var cycleCondition = new EventTriggerCondition<CardCycledEvent>(
            (e, _) =>
                ReferenceEquals(e.Card, card)
                && ReferenceEquals(e.Player, owner));

        var cycleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cycleCondition,
            effects: new IEffect[] { tutorEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(cycleTrigger);
        triggers?.RegisterTriggeredAbility(cycleTrigger);

        return card;
    }
}
