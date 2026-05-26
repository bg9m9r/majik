using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Monstrous Carabid (Onslaught, {4}{B}).
///
/// Creature — Insect 4/1. Oracle text (Scryfall):
///   "Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Insect {4}{B} 4/1</b>. Plain-vanilla Onslaught
///   "cycler with stats", printed almost exclusively to be discarded
///   for a card and reanimated by Living End / Hypnox / Mind's Desire
///   style payoffs. Same shape role as Twisted Abomination + Krosan
///   Tusker in the same block.
/// - <b>Cycling {2}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{2}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers <see cref="DiscardSelfCost"/>
///   (CR 702.32a hand-zone gate) on the cost stack, and on resolve
///   draws a card then publishes <see cref="CardCycledEvent"/> for
///   CR 702.32d subscribers (Lightning Rift, Astral Slide, Curator of
///   Mysteries, the Living End cascade chain). The vanilla pip cost
///   makes this the cheapest body in the cycler suite — Carabid's
///   appeal is the "any deck, any mana" cycle.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Cycling activated
///   ability attached without an event bus (no
///   <see cref="CardCycledEvent"/> publication). Suitable for
///   dispatcher / shape / cost-stack tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling
///   resolve publishes <see cref="CardCycledEvent"/> so the
///   "Whenever a player cycles" subscribers fire.
///
/// CR rule references: 205.3m (Insect subtype), 702.32 (Cycling).
/// </summary>
[CardName("Monstrous Carabid")]
public static class MonstrousCarabidFactory
{
    public const string CardName = "Monstrous Carabid";
    public const string PrintedManaCost = "{4}{B}";
    public const int Power = 4;
    public const int Toughness = 1;
    public const string CyclingCost = "{2}";

    /// <summary>
    /// Construct Monstrous Carabid with no event bus. The cycling
    /// activated ability is attached to the card shape; activation is
    /// gated to the controller's hand by
    /// <see cref="DiscardSelfCost.CanPay"/>. Shape-only — no
    /// <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Monstrous Carabid. When <paramref name="eventBus"/>
    /// is supplied the cycling resolve body publishes
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
            subtypes: new[] { CardSubtype.Insect });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cycling {2} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
