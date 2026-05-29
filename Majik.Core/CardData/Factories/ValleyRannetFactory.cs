using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Valley Rannet (Modern Horizons 3, {4}{R}{G}).
///
/// Creature — Beast 6/3. Oracle text (Scryfall, verified):
///   "Mountaincycling {2}, forestcycling {2} ({2}, Discard this card:
///    Search your library for a Mountain or Forest card, reveal it, put
///    it into your hand, then shuffle.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Beast {4}{R}{G} 6/3</b>, mana value 6. Red + green
///   from the {R}{G} pips (CR 105.2). Owner / controller stamped.
///
/// - <b>Mountaincycling {2} + Forestcycling {2}</b> (CR 702.29 / 702.32d) —
///   each routed through the shared <see cref="TypedCyclingFactory.Build"/>
///   primitive with cycle cost <see cref="ManaCostCost"/>("{2}"). Per the
///   printed reminder text BOTH abilities search for "a Mountain or Forest
///   card", so both use the same predicate
///   <c>c =&gt; c.HasSubtype(CardSubtype.Mountain) ||
///   c.HasSubtype(CardSubtype.Forest)</c>. The two keywords differ only in
///   their typed marker name (oracle audits / bot decision layer); the
///   tutor pool is identical. Each primitive attaches the
///   <see cref="ActivatedAbility"/> + the typed
///   <see cref="KeywordAbility"/> marker ("Mountaincycling" /
///   "Forestcycling") + the generic "Cycling" marker (CR 702.32d —
///   typecycling IS Cycling), layers <see cref="DiscardSelfCost"/>
///   (CR 702.32a hand-zone gate), and on resolve tutors the chosen
///   Mountain/Forest card from the controller's library to hand
///   (agent prompt with deterministic first-match fallback — CR 701.19a) +
///   shuffles (CR 701.20a) + publishes <see cref="CardCycledEvent"/>
///   (CR 702.32d) when an event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Both cycling
///   abilities attached without an event bus (no
///   <see cref="CardCycledEvent"/> publication). Suitable for dispatcher /
///   shape tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling
///   resolve publishes <see cref="CardCycledEvent"/> so CR 702.32d
///   "Whenever a player cycles" triggers fire.
///
/// CR rule references: 105.2 (color from pips), 202.3 (mana value),
/// 205.3m (Beast subtype), 205.4a (Mountain / Forest basic-land
/// subtypes), 701.19a (library search), 701.20a (shuffle), 702.29
/// (Mountaincycling), 702.32 (Cycling), 702.32d (typecycling).
/// </summary>
[CardName("Valley Rannet")]
public static class ValleyRannetFactory
{
    public const string CardName = "Valley Rannet";
    public const string PrintedManaCost = "{4}{R}{G}";
    public const int Power = 6;
    public const int Toughness = 3;
    public const string CyclingCost = "{2}";

    /// <summary>
    /// Construct Valley Rannet with no event bus. Both typecycling
    /// activated abilities are attached to the card shape; activation is
    /// gated to the controller's hand by
    /// <see cref="DiscardSelfCost.CanPay"/>. Shape-only — no
    /// <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Valley Rannet. When <paramref name="eventBus"/> is
    /// supplied each typecycling resolve body publishes
    /// <see cref="CardCycledEvent"/> so CR 702.32d "Whenever a player
    /// cycles a card" triggers fire.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
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

        // --------------------------------------------------------------------
        // Mountaincycling {2} + Forestcycling {2} — CR 702.29 / 702.32d.
        // The printed reminder text reads "Search your library for a Mountain
        // OR Forest card" for BOTH abilities, so both share the same tutor
        // predicate (Mountain-subtype OR Forest-subtype). The two abilities
        // differ only in their typed keyword marker. Each is routed through
        // the shared TypedCyclingFactory primitive, which appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a), attaches the typed
        // keyword + the generic "Cycling" marker (CR 702.32d), and on resolve
        // tutors via agent-driven pick / deterministic first-match fallback
        // (CR 701.19a) + shuffles (CR 701.20a) + publishes CardCycledEvent
        // (CR 702.32d) when an event bus is supplied.
        // --------------------------------------------------------------------
        static bool MountainOrForest(ICard c) =>
            c.HasSubtype(CardSubtype.Mountain) || c.HasSubtype(CardSubtype.Forest);

        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: MountainOrForest,
            typedKeyword: "Mountaincycling",
            kindLabel: "Mountain or Forest card",
            eventBus: eventBus);

        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: MountainOrForest,
            typedKeyword: "Forestcycling",
            kindLabel: "Mountain or Forest card",
            eventBus: eventBus);

        return card;
    }
}
