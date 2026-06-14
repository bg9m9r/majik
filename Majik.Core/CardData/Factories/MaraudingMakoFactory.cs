using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Marauding Mako (Outlaws of Thunder Junction,
/// {R}).
///
/// Creature — Shark Pirate 1/1. Oracle text (Scryfall, verified):
///   "Whenever you discard one or more cards, put that many +1/+1
///    counters on this creature.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// A red one-drop that snowballs every time its controller discards —
/// pairs naturally with rummage / loot / cycling effects (including its
/// OWN cycling, fired by another copy).
///
/// ## Shape source
/// Card identity (name, {R}, 1/1, Creature — Shark Pirate) is loaded from
/// <c>Majik.Core/CardData/Cards/marauding-mako.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/>. The discard trigger and
/// Cycling are attached in code below — the JSON ability schema does not
/// yet express a discard-linked counter trigger or keyword markers.
///
/// ## Implemented (v1)
///
/// - <b>1/1 Creature — Shark Pirate at {R}.</b>
///
/// - <b>Discard trigger (CR 603.1).</b> "Whenever you discard one or more
///   cards, put that many +1/+1 counters on this creature." The engine
///   has no dedicated <c>DiscardedEvent</c> (see
///   <see cref="ContainmentConstructFactory"/> / <see cref="NecropotenceFactory"/>);
///   discards funnel through <see cref="CardMovedEvent"/> with
///   <c>FromZone == Hand &amp;&amp; ToZone == Graveyard</c>, one event PER
///   card. The trigger filters that funnel to cards owned by the Mako's
///   controller ("you discard" — CR 109.5) and, on each matching event,
///   puts one +1/+1 counter on the Mako via
///   <see cref="CountersService.Add"/> (CR 122.1 — routed through the
///   <see cref="ReplacementBus"/> so Hardened Scales / Doubling Season
///   can rewrite the count per CR 614, and publishing
///   <see cref="CounterAddedEvent"/> so downstream payoffs chain).
///
///   <para><b>"That many" via per-card funnelling.</b> Because each
///   discarded card publishes its own <see cref="CardMovedEvent"/>, a
///   batch discard of N cards fires the trigger N times, placing N
///   counters total — the observable end state matches the printed "put
///   that many +1/+1 counters" (CR 701.8 — discarding multiple cards).
///   The only divergence from a true single batch trigger is N separate
///   <see cref="CounterAddedEvent"/> publications instead of one; that is
///   within the v1 acceptable-shape envelope (it only matters to a
///   hypothetical "whenever one or more counters are put on a creature"
///   payoff that wants exactly-one-trigger semantics — none ship today).</para>
///
///   <para><b>No nonland gate.</b> Unlike Containment Construct ("nonland
///   card"), Marauding Mako counts EVERY discarded card — lands included
///   (CR 701.8 — "discard one or more cards" is type-agnostic). The
///   filter therefore does not exclude <see cref="CardType.Land"/>.</para>
///
/// - <b>Cycling {2}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with a generic {2}
///   <see cref="ManaCostCost"/>. The primitive attaches the
///   <see cref="ActivatedAbility"/> + a "Cycling" <see cref="KeywordAbility"/>
///   marker, layers <see cref="DiscardSelfCost"/> (CR 702.32a hand-zone
///   gate) on the cost stack, and on resolve draws a card then publishes
///   <see cref="CardCycledEvent"/>. NOTE: cycling discards the Mako ITSELF
///   from hand — a single Mako on the battlefield does not grow from its
///   own cycling (the card being cycled is in hand, not on the
///   battlefield, and Mako's trigger functions only from the battlefield
///   per CR 113.6). A separate Mako on the battlefield WOULD see the
///   cycling discard and grow (the discarded copy is its controller's
///   card).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Discard trigger +
///   Cycling attached as markers; no event-bus subscriptions, so no
///   counters accrue and no <see cref="CardCycledEvent"/> publishes.
///   Suitable for dispatcher / shape / cost-stack tests.
/// - <see cref="Create(Player, IEventBus?, ReplacementBus?)"/> — fully
///   wired. The discard watcher subscribes so the "discard → +1/+1
///   counter" loop runs end to end; counter placement routes through the
///   optional <see cref="ReplacementBus"/>; cycling publishes
///   <see cref="CardCycledEvent"/>.
///
/// CR rule references: 205.3m (Shark / Pirate subtypes), 603.1 (trigger),
/// 701.8 (discard), 122.1 / 614 (counters), 702.32 (Cycling).
/// </summary>
[CardName("Marauding Mako")]
public static class MaraudingMakoFactory
{
    public const string CardName = "Marauding Mako";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("marauding-mako");

    /// <summary>+1/+1 counters placed per discarded card (CR 122.1).</summary>
    public const int CountersPerDiscard = 1;

    /// <summary>Cycling cost — {2} generic (CR 702.32).</summary>
    public const string CyclingCost = "{2}";

    /// <summary>
    /// Construct Marauding Mako with no live event-bus wiring (the shape /
    /// dispatcher path). The discard trigger is attached as a marker and
    /// Cycling is attached, but the discard watcher is NOT subscribed, so
    /// no counters accrue and no <see cref="CardCycledEvent"/> publishes.
    /// Suitable for factory-shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Marauding Mako. When <paramref name="eventBus"/> is
    /// supplied the discard watcher subscribes so the "you discard one or
    /// more cards → +1/+1 counter(s)" loop runs end to end, and Cycling
    /// publishes <see cref="CardCycledEvent"/>. When
    /// <paramref name="replacements"/> is supplied the counter placement
    /// routes through it so Hardened Scales / Doubling Season can rewrite
    /// the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Discard trigger — CR 603.1.
        //   "Whenever you discard one or more cards, put that many +1/+1
        //    counters on this creature."
        //
        // The engine has no dedicated DiscardedEvent; discards funnel
        // through CardMovedEvent with FromZone == Hand && ToZone ==
        // Graveyard, one event PER card (see ContainmentConstructFactory /
        // NecropotenceFactory). Each matching event places one +1/+1
        // counter; a batch discard of N cards thus places N counters in
        // total ("that many"). Unlike Containment Construct there is NO
        // nonland gate — CR 701.8 "discard one or more cards" counts every
        // card type, lands included.
        // ----------------------------------------------------------------
        bool IsControllerDiscard(CardMovedEvent e)
        {
            if (e.FromZone != ZoneType.Hand) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            // "You discard" — gate to the Mako's controller (CR 109.5).
            // The discarded card's owner is the discarder.
            return ReferenceEquals(e.Card.Owner, card.Controller ?? owner);
        }

        // Marker triggered ability so factory-shape / dispatch tests can
        // assert the discard trigger is attached. The actual counter
        // placement is driven by the event-bus subscription below.
        var triggerMarker = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) => IsControllerDiscard(e)),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: put a +1/+1 counter on it (you discarded a card)",
                    () => CountersService.Add(
                        card, CounterType.PlusOnePlusOne, CountersPerDiscard, replacements, eventBus)),
            },
            // CR 113.6 — abilities on permanent cards function from the
            // battlefield only. A Mako in hand / graveyard does not fire.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(triggerMarker);

        if (eventBus != null)
        {
            eventBus.Subscribe<CardMovedEvent>(e =>
            {
                if (!IsControllerDiscard(e)) return;
                // CR 113.6 — only fire while the Mako is on the
                // battlefield (the discarded card itself, in hand→
                // graveyard transit, is not on the battlefield).
                if (card.Zone != ZoneType.Battlefield) return;
                // CR 122.1 / 614 — route through the replacement bus so
                // Hardened Scales / Doubling Season can rewrite the count.
                CountersService.Add(
                    card, CounterType.PlusOnePlusOne, CountersPerDiscard, replacements, eventBus);
            });
        }

        // ----------------------------------------------------------------
        // Cycling {2} — CR 702.32. Routed through the shared CyclingFactory
        // primitive; the primitive appends the DiscardSelfCost hand-zone
        // gate (CR 702.32a) and the CardCycledEvent publish (CR 702.32d)
        // automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
