using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Desert Cerodon (Hour of Devastation, {5}{R}).
///
/// Creature — Beast 6/4. Oracle text (Scryfall):
///   "Cycling {R} ({R}, Discard this card: Draw a card.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Beast {5}{R} 6/4</b>. Plain-vanilla "cycler with
///   stats" — a big body you can hard-cast late or pitch early for a
///   single red. Same shape role as <see cref="MonstrousCarabidFactory"/>
///   (Onslaught), differing only in stats (6/4) and cycle cost ({R}).
/// - <b>Cycling {R}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{R}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers <see cref="DiscardSelfCost"/>
///   (CR 702.32a hand-zone gate) on the cost stack, and on resolve
///   draws a card then publishes <see cref="CardCycledEvent"/> for
///   CR 702.32d subscribers (Lightning Rift, Astral Slide, etc.).
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
/// CR rule references: 205.3m (Beast subtype), 702.32 (Cycling).
/// </summary>
[CardName("Desert Cerodon")]
public static class DesertCerodonFactory
{
    public const string CardName = "Desert Cerodon";
    public const string PrintedManaCost = "{5}{R}";
    public const int Power = 6;
    public const int Toughness = 4;
    public const string CyclingCost = "{R}";

    /// <summary>
    /// Construct Desert Cerodon with no event bus. The cycling activated
    /// ability is attached to the card shape; activation is gated to the
    /// controller's hand by <see cref="DiscardSelfCost.CanPay"/>.
    /// Shape-only — no <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Desert Cerodon. When <paramref name="eventBus"/> is
    /// supplied the cycling resolve body publishes
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

        // ----------------------------------------------------------------
        // Cycling {R} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
