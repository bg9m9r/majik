using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pale Recluse (Future Sight, {4}{G}{W}).
///
/// Creature — Spider 4/5. Oracle text (Scryfall):
///   "Reach (This creature can block creatures with flying.)
///    Forestcycling {2}, plainscycling {2} ({2}, Discard this card:
///    Search your library for a Forest or Plains card, reveal it, put it
///    into your hand, then shuffle.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Spider {4}{G}{W} 4/5</b>. A Gruul-on-paper-but-Selesnya
///   gold body whose draw is the dual land tutor, not the stats.
/// - <b>Reach</b> (CR 702.17) — <see cref="KeywordAbility"/> marker
///   consumed by <see cref="Majik.Core.Combat.CombatAbilities.HasReach"/>
///   (same wiring shape as <see cref="GenerousEntFactory"/>).
/// - <b>Forestcycling {2}</b> + <b>Plainscycling {2}</b> (CR 702.32d) —
///   the printed combined line "Forestcycling {2}, plainscycling {2}" is
///   two distinct typecycling abilities sharing one reminder ("a Forest
///   or Plains card"). Each routes through the shared
///   <see cref="TypedCyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{2}"):
///   <list type="bullet">
///     <item>Forestcycling — predicate <c>c =&gt; c.HasSubtype(CardSubtype.Forest)</c>.</item>
///     <item>Plainscycling — predicate <c>c =&gt; c.HasSubtype(CardSubtype.Plains)</c>.</item>
///   </list>
///   Each primitive call appends the <see cref="DiscardSelfCost"/>
///   hand-zone gate (CR 702.32a), attaches the typed keyword marker
///   ("Forestcycling" / "Plainscycling") + the generic "Cycling" marker
///   (CR 702.32d — typecycling IS Cycling), and on resolve tutors the
///   first matching card from the controller's library to hand (agent
///   prompt with deterministic first-match fallback — CR 701.19a) +
///   shuffles (CR 701.20a) + publishes <see cref="CardCycledEvent"/>
///   (CR 702.32d). Exact analogue of <see cref="GenerousEntFactory"/>
///   (single Forestcycling) and <see cref="TrollOfKhazadDumFactory"/>
///   (single Swampcycling), composed twice for the dual-type line.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Both typecycling
///   abilities attached without an event bus (no
///   <see cref="CardCycledEvent"/> publication). Suitable for
///   dispatcher / shape / Reach targeting tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Each
///   typecycling resolve publishes <see cref="CardCycledEvent"/> so
///   CR 702.32d "Whenever a player cycles" triggers fire.
///
/// CR rule references: 205.3m (Spider subtype), 205.4a (Forest / Plains
/// basic-land subtypes), 701.19a (library search), 701.20a (shuffle),
/// 702.17 (Reach), 702.32 (Cycling), 702.32d (typecycling).
/// </summary>
[CardName("Pale Recluse")]
public static class PaleRecluseFactory
{
    public const string CardName = "Pale Recluse";
    public const string PrintedManaCost = "{4}{G}{W}";
    public const int Power = 4;
    public const int Toughness = 5;
    public const string CyclingCost = "{2}";

    /// <summary>
    /// Construct Pale Recluse with no event bus. Both typecycling
    /// activated abilities are attached to the card shape; activation is
    /// gated to the controller's hand by <see cref="DiscardSelfCost.CanPay"/>.
    /// Shape-only — no <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Pale Recluse. When <paramref name="eventBus"/> is
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
            subtypes: new[] { CardSubtype.Spider });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.17 — Reach. KeywordAbility marker consumed by
        // CombatAbilities.HasReach (mirrors Generous Ent / Endurance).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        // ----------------------------------------------------------------
        // Forestcycling {2} — CR 702.32d. First of the two typecycling
        // abilities on the combined printed line. Tutors a Forest card.
        // ----------------------------------------------------------------
        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: c => c.HasSubtype(CardSubtype.Forest),
            typedKeyword: "Forestcycling",
            kindLabel: "Forest card",
            eventBus: eventBus);

        // ----------------------------------------------------------------
        // Plainscycling {2} — CR 702.32d. Second typecycling ability on
        // the same printed line. Tutors a Plains card. Routed through the
        // same primitive; the duplicate generic "Cycling" marker it adds
        // is harmless (keyword presence is set-membership, not a count).
        // ----------------------------------------------------------------
        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: c => c.HasSubtype(CardSubtype.Plains),
            typedKeyword: "Plainscycling",
            kindLabel: "Plains card",
            eventBus: eventBus);

        return card;
    }
}
