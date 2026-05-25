using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Street Wraith (Future Sight, {3}{B}{B}).
///
/// Creature — Zombie 3/4. Oracle text:
///   "Swampwalk (This creature can't be blocked as long as defending
///    player controls a Swamp.)
///    Cycling—Pay 2 life. (Pay 2 life, Discard this card: Draw a card.)"
///
/// Street Wraith's "free" cycling (no mana cost — only "Pay 2 life" +
/// the implicit "Discard this card" of every Cycling ability per
/// CR 702.32a) is the printed identity of the card; the body's P/T and
/// Swampwalk keyword exist mostly so it has a creature shape if it ever
/// reaches the battlefield via Reanimator-style cheats.
///
/// ## Implemented (v1)
/// - <b>Creature — Zombie</b> 3/4 {3}{B}{B} with owner / controller wiring.
/// - <b>Swampwalk</b> as a <see cref="KeywordAbility"/> marker (CR 702.13
///   landwalk variant; <see cref="Majik.Core.Combat.CombatAbilities"/>
///   consumers gate the "can't be blocked" predicate on whether the
///   defending player controls a Swamp).
/// - <b>Cycling — Pay 2 life</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="PayLifeCost"/>(2). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers the <see cref="DiscardSelfCost"/> hand-zone
///   gate (CR 702.32a) onto the cost stack, and on resolve publishes
///   <see cref="CardCycledEvent"/> for any "Whenever a player cycles"
///   triggers (CR 702.32d — Lightning Rift, Astral Slide, Decree of
///   Justice, etc.). The pay-life cost gates on <c>LifeTotal &gt;= 2</c>
///   (CR 119.4).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Cycling activated
///   ability attached for structural observability; the cycling resolve
///   body has no event bus and so doesn't publish
///   <see cref="CardCycledEvent"/>. Suitable for dispatcher / shape /
///   isolated cost-shape tests.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. Cycling
///   resolve publishes <see cref="CardCycledEvent"/> against the supplied
///   bus so Lightning Rift / Astral Slide / Decree of Justice triggers
///   (CR 702.32d) fire automatically.
/// </summary>
[CardName("Street Wraith")]
public static class StreetWraithFactory
{
    public const string CardName = "Street Wraith";
    public const string PrintedManaCost = "{3}{B}{B}";
    public const int Power = 3;
    public const int Toughness = 4;
    public const int CyclingLifeCost = 2;

    /// <summary>
    /// Construct Street Wraith with no event bus. The cycling activated
    /// ability is attached to the card shape; activation is gated to
    /// the controller's hand by <see cref="DiscardSelfCost.CanPay"/>
    /// and to <c>LifeTotal &gt;= 2</c> by <see cref="PayLifeCost.CanPay"/>.
    /// Shape-only path — no <see cref="CardCycledEvent"/> publication.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Street Wraith. When <paramref name="eventBus"/> is
    /// supplied the cycling resolve body publishes
    /// <see cref="CardCycledEvent"/> so CR 702.32d "Whenever a player
    /// cycles a card" triggers (Lightning Rift, Astral Slide) fire.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Zombie });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Swampwalk — CR 702.13 (landwalk). KeywordAbility marker only;
        // CombatAbilities consumers gate the "can't be blocked" predicate
        // on whether the defending player controls a Swamp.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Swampwalk", card, owner));

        // ----------------------------------------------------------------
        // Cycling — Pay 2 life. (CR 702.32)
        //   "Pay 2 life, Discard this card: Draw a card."
        // Routed through the shared CyclingFactory primitive — cycle cost
        // is PayLifeCost(CyclingLifeCost), the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new PayLifeCost(CyclingLifeCost), eventBus);

        return card;
    }
}
