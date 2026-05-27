using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tribute Mage (Modern Horizons, {2}{U}).
///
/// Creature — Human Wizard 2/2. Oracle text (Scryfall):
///   "When Tribute Mage enters, you may search your library for an
///    artifact card with mana value 2, reveal that card, put it into
///    your hand, then shuffle."
///
/// Sibling of <see cref="TrinketMageFactory"/> (Fifth Dawn, MV ≤ 1) —
/// same tutor primitive, different mana-value predicate. Tribute Mage
/// is the "MV 2" rung of the Modern Horizons "X Mage" cycle (Trinket
/// Mage ≤ 1, Tribute Mage == 2, Trophy Mage == 3, Treasure Mage ≥ 5),
/// commonly cast for Aether Spellbomb / Pithing Needle / Soul-Guide
/// Lantern / Talisman / Astrolabe / Sword of the Meek + Thopter
/// Foundry combo enabling.
///
/// ## Implemented (v1)
///
/// - <b>Creature — Human Wizard {2}{U} 2/2</b>.
/// - <b>ETB tutor (CR 701.19a / 603.1)</b>: When Tribute Mage enters,
///   the controller's library is searched deterministically for the
///   first artifact card with <see cref="ValueObjects.ManaCost.TotalValue"/>
///   == 2; if found, it is moved Library → Hand and the library is
///   shuffled via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a). Per CR 701.19a the search is a "may"; the v1
///   deterministic picker simply takes the first eligible card. The
///   single-arg factory attaches the trigger to the card but does NOT
///   register it with a TriggerManager — tests exercise the ETB
///   effect by firing the trigger manually or driving the card
///   through <see cref="Majik.Core.Services.ZoneService"/> (which
///   publishes the <see cref="CardMovedEvent"/> the trigger
///   consumes). Same shape as Trinket Mage.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Reveal event</b>: the ETB tutor moves the card to hand without
///   emitting a CardRevealedEvent. Wire a reveal when
///   CardRevealedEvent plumbing is exercised by an in-engine prompt
///   path.
/// - <b>Agent prompt for "you may"</b>: the deterministic picker
///   auto-takes the first eligible card; a full implementation would
///   prompt the controller for the choice (including declining).
/// </summary>
[CardName("Tribute Mage")]
public static class TributeMageFactory
{
    public const string CardName = "Tribute Mage";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Tutor predicate mana-value target — Tribute Mage
    /// prints "mana value 2".</summary>
    public const int TutorManaValue = 2;

    /// <summary>
    /// Construct Tribute Mage with no live TriggerManager wiring (the
    /// shape / dispatcher path). The ETB trigger is attached but not
    /// registered — suitable for unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Tribute Mage with optional runtime services. When
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
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Tribute Mage enters, you may search your library for
        //    an artifact card with mana value 2, reveal that card, put
        //    it into your hand, then shuffle."
        // v1: deterministic — take the first artifact card in the
        // library whose mana value is exactly 2. CR 701.20a shuffle is
        // wired via LibraryShuffle (publishes a LibraryShuffledEvent
        // when an EventBus is registered). Reveal-event emission is
        // the only outstanding gap (see class xmldoc).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor an artifact with mana value {TutorManaValue} to hand",
            () =>
            {
                var pick = owner.Zones.Library.GetCards()
                    .OfType<Card>()
                    .FirstOrDefault(c => c.HasType(CardType.Artifact)
                        && c.ManaCostValue.TotalValue == TutorManaValue);
                if (pick == null) return; // CR 701.19a — declined / no candidate is legal.

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                // CR 701.20a — shuffle after the search resolves.
                Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(owner, "tribute-mage");
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
