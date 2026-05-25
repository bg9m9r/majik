using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Trinket Mage (Fifth Dawn, {2}{U}).
///
/// Creature — Human Wizard 2/2. Oracle text:
///   "When Trinket Mage enters, you may search your library for an artifact
///    card with mana value 1 or less, reveal that card, put it into your
///    hand, then shuffle."
///
/// ## Implemented (v1)
/// - 2/2 Human Wizard with mana cost {2}{U}.
/// - <b>ETB tutor (CR 701.19a / 603.1)</b>: When Trinket Mage enters,
///   the controller's library is searched deterministically for the first
///   artifact card with <see cref="ValueObjects.ManaCost.TotalValue"/> ≤ 1;
///   if found, it is moved Library → Hand. Per CR 701.19a the search is a
///   "may" and the v1 deterministic picker simply takes the first eligible
///   card. The single-arg factory attaches the trigger to the card but does
///   NOT register it with a TriggerManager; tests exercise the ETB effect
///   by either firing the trigger manually or driving the card through
///   ZoneService (which publishes the <see cref="CardMovedEvent"/> that the
///   trigger consumes).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the ETB tutor moves the card to hand without
///   emitting a CardRevealedEvent. Wire a reveal when CardRevealedEvent
///   plumbing is exercised by an in-engine prompt path.
/// - <b>Agent prompt for "you may"</b>: the deterministic picker auto-takes
///   the first eligible card; a full implementation would prompt the
///   controller for the choice (including declining).
/// </summary>
[CardName("Trinket Mage")]
public static class TrinketMageFactory
{
    /// <summary>
    /// Construct Trinket Mage with no live TriggerManager wiring (the
    /// shape/dispatcher path). The ETB trigger is attached but not
    /// registered — suitable for unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Trinket Mage with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a <see cref="CardMovedEvent"/> to the battlefield
    /// places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Trinket Mage",
            manaCost: "{2}{U}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Trinket Mage enters, you may search your library for an
        //    artifact card with mana value 1 or less, reveal that card,
        //    put it into your hand, then shuffle."
        // v1: deterministic — take the first artifact card in the library
        // whose mana value is ≤ 1. CR 701.20a shuffle is wired via
        // LibraryShuffle (publishes a LibraryShuffledEvent when an EventBus
        // is registered). Reveal-event emission is the only outstanding
        // gap (see class xmldoc).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Trinket Mage: tutor an artifact with mana value 1 or less to hand",
            () =>
            {
                var pick = owner.Zones.Library.GetCards()
                    .OfType<Card>()
                    .FirstOrDefault(c => c.HasType(CardType.Artifact)
                        && c.ManaCostValue.TotalValue <= 1);
                if (pick == null) return; // CR 701.19a — declined / no candidate is legal.

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                // CR 701.20a — shuffle after the search resolves.
                Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(owner, "trinket-mage");
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
