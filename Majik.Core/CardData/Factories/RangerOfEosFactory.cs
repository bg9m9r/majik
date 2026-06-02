using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ranger of Eos (Shards of Alara, {3}{W}).
///
/// Creature — Human Soldier Ranger, 3/2. Oracle text (per Scryfall):
///   "When this creature enters, you may search your library for up to two
///    creature cards with mana value 1 or less, reveal them, put them into
///    your hand, then shuffle."
///
/// ## Implemented (v1)
/// - 3/2 Human Soldier Ranger with mana cost {3}{W}.
/// - <b>ETB tutor (CR 603.6a / 701.19a)</b>: When Ranger of Eos enters, the
///   controller's library is searched deterministically for the first
///   <b>two</b> creature cards with mana value &le; 1; each found card is
///   moved Library → Hand, then the library is shuffled (CR 701.20a) via
///   <see cref="LibraryShuffle.ShuffleLibrary"/>. This is the
///   <see cref="RangerCaptainOfEosFactory"/> tutor adapted from a single
///   fetch to the up-to-two cap — the only behavioural difference between
///   the two cards' ETB. v1 picker is the deterministic first-eligible
///   pattern Stoneforge Mystic / Eldritch Evolution / Ranger-Captain use;
///   agent-driven "you may" choice + reveal-event emission are deferred
///   alongside the rest of the tutor family. CR 202.3 mana-value gate reads
///   <see cref="Card.ManaCostValue"/>.<c>TotalValue</c>, the canonical mv
///   accessor used elsewhere in the engine.
///
/// ## Deferred (v1 gaps — shared with the whole tutor family)
/// - <b>Agent-driven tutor prompt</b>: the ETB body picks the first (up to)
///   two eligible creature cards in the controller's library
///   deterministically. A full implementation would prompt the controller's
///   agent (CR 701.19a) for which mv-&le;-1 creatures to fetch (0, 1, or 2),
///   including the "you may" / "up to" opt-out clauses.
/// - <b>Reveal event</b>: the ETB tutor moves the cards to hand without
///   emitting a <c>CardRevealedEvent</c> ("reveal them"). Same gap as
///   Stoneforge Mystic's and Ranger-Captain's tutors.
/// </summary>
[CardName("Ranger of Eos")]
public static class RangerOfEosFactory
{
    public const string CardName = "Ranger of Eos";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 3;
    public const int Toughness = 2;
    public const int TutorMaxManaValue = 1;
    public const int TutorMaxCards = 2;

    /// <summary>
    /// Construct Ranger of Eos with no live <see cref="TriggerManager"/>
    /// wiring. The ETB trigger is attached to the card shape but not
    /// registered; suitable for card-shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Ranger of Eos. When <paramref name="triggers"/> is supplied,
    /// the ETB tutor trigger is registered so a <c>CardMovedEvent</c> to the
    /// battlefield places it on the stack automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[]
            {
                CardSubtype.Human,
                CardSubtype.Soldier,
                CardSubtype.Ranger,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / 701.19a.
        //   "When this creature enters, you may search your library for up
        //    to two creature cards with mana value 1 or less, reveal them,
        //    put them into your hand, then shuffle."
        // v1: deterministic first-two-eligible picker (mirrors
        // RangerCaptainOfEos's single-card fetch, raised to the up-to-two
        // cap). CR 202.3 mv gate reads ManaCostValue.TotalValue.
        // CR 701.20a shuffle via LibraryShuffle.ShuffleLibrary, which runs
        // even on an empty/declined search per the printed "then shuffle".
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor up to two creature cards with mana value 1 or less to hand",
            () =>
            {
                var picks = owner.Zones.Library.GetCards()
                    .OfType<Card>()
                    .Where(c => c.HasType(CardType.Creature)
                                && c.ManaCostValue.TotalValue <= TutorMaxManaValue)
                    .Take(TutorMaxCards)
                    .ToList();

                foreach (var pick in picks)
                {
                    owner.Zones.Library.RemoveCard(pick);
                    owner.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }

                // CR 701.20a — shuffle always happens ("then shuffle"),
                // even when zero or one card was found.
                LibraryShuffle.ShuffleLibrary(owner, "ranger-of-eos");
            });

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
}
