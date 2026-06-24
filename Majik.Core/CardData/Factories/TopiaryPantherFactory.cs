using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Topiary Panther (Modern Horizons 3, {4}{G}{G}).
///
/// Creature — Plant Cat 6/5. Oracle text (verified against Scryfall):
///   "Trample
///    Basic landcycling {1}{G} ({1}{G}, Discard this card: Search your
///    library for a basic land card, reveal it, put it into your hand, then
///    shuffle.)"
///
/// ## Shape source
/// Card identity (name, {4}{G}{G}, 6/5, Creature — Plant Cat) AND the Trample
/// keyword marker are loaded from
/// <c>Majik.Core/CardData/Cards/topiary-panther.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — the JSON <c>keywords</c> array
/// auto-attaches Trample (CR 702.19) as a <see cref="KeywordAbility"/> marker.
/// Only the Basic landcycling activated ability is layered on in code below
/// (the JSON ability schema does not express the typed-cycling primitive).
///
/// ## Implemented (v1)
/// - <b>Creature — Plant Cat {4}{G}{G} 6/5</b>, owner / controller wired.
/// - <b>Trample (CR 702.19)</b> — <see cref="KeywordAbility"/> marker via the
///   JSON <c>keywords</c> array; read by the combat-damage assignment
///   subsystem.
/// - <b>Basic landcycling {1}{G}</b> (CR 702.32d — typecycling) — routed
///   through the shared <see cref="TypedCyclingFactory.Build"/> primitive with
///   cycle cost <see cref="ManaCostCost"/>("{1}{G}") and predicate
///   <c>c =&gt; c.HasType(CardType.Land) &amp;&amp; c.HasSupertype(CardSupertype.Basic)</c>
///   for the "basic land card" tutor target (CR 305.6 — Land card type +
///   Basic supertype; same predicate Krosan Tusker's on-cycle basic-land
///   rider uses). The primitive attaches the <see cref="ActivatedAbility"/> +
///   a <see cref="KeywordAbility"/> ("Basic landcycling") typed marker + a
///   generic "Cycling" marker (CR 702.32d — typecycling IS Cycling), layers
///   <see cref="DiscardSelfCost"/> (CR 702.32a hand-zone gate) on the cost
///   stack, and on resolve tutors the first basic land card from the
///   controller's library to hand (agent prompt with deterministic
///   first-match fallback — CR 701.19a) + shuffles (CR 701.20a) + publishes
///   <see cref="CardCycledEvent"/> for the CR 702.32d "Whenever a player
///   cycles" subscribers (Lightning Rift, Astral Slide, etc.). Mirrors
///   <see cref="LorienRevealedFactory"/>'s Islandcycling wiring with a
///   basic-land predicate.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + Trample marker. Basic
///   landcycling ability attached with no event bus (no
///   <see cref="CardCycledEvent"/> publication). The overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Basic landcycling
///   resolve publishes <see cref="CardCycledEvent"/> so CR 702.32d "Whenever a
///   player cycles" triggers fire.
///
/// CR rule references: 205.3m (Plant / Cat subtypes), 205.4a (Basic
/// supertype), 305.6 (basic land), 701.19a (library search), 701.20a
/// (shuffle), 702.19 (Trample), 702.32 (Cycling), 702.32d (typecycling).
/// </summary>
[CardName("Topiary Panther")]
public static class TopiaryPantherFactory
{
    public const string CardName = "Topiary Panther";
    public const string Slug = "topiary-panther";
    public const string CyclingCost = "{1}{G}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Topiary Panther with no event bus. The Basic landcycling
    /// activated ability is attached to the card shape; activation is gated to
    /// the controller's hand by <see cref="DiscardSelfCost.CanPay"/>.
    /// Shape-only — no <see cref="CardCycledEvent"/> publication. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Build Topiary Panther. When <paramref name="eventBus"/> is supplied the
    /// Basic landcycling resolve body publishes <see cref="CardCycledEvent"/>
    /// so CR 702.32d "Whenever a player cycles" triggers fire.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Basic landcycling {1}{G} — CR 702.32d. Routed through the shared
        // TypedCyclingFactory primitive with predicate
        //   c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic)
        // for the "basic land card" tutor target (CR 305.6 — Land card type +
        // Basic supertype). The primitive appends the DiscardSelfCost
        // hand-zone gate (CR 702.32a), attaches both the "Basic landcycling"
        // typed keyword + the generic "Cycling" marker (CR 702.32d —
        // typecycling IS Cycling), and on resolve tutors a basic land card via
        // agent prompt with deterministic first-match fallback (CR 701.19a) +
        // shuffles (CR 701.20a) + publishes CardCycledEvent (CR 702.32d).
        // ----------------------------------------------------------------
        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: c =>
                c.HasType(CardType.Land)
                && c.HasSupertype(CardSupertype.Basic),
            typedKeyword: "Basic landcycling",
            kindLabel: "basic land card",
            eventBus: eventBus);

        return card;
    }
}
