using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Street Wraith (Future Sight / Modern Horizons 3).
///
/// Creature — Wraith {3}{B}{B}, 3/4. Oracle text:
///   "Swampwalk. Cycling—Pay 2 life."
///
/// ## Implemented (v1)
/// - 3/4 Creature — Wraith, mana cost {3}{B}{B}, owner/controller wired.
/// - <b>Swampwalk</b> (CR 702.14) — wired as a <see cref="KeywordAbility"/>
///   marker ("Swampwalk"). Combat-validator enforcement (check whether
///   defending player controls a Swamp) is deferred — same posture as
///   Islandwalk on Lord of Atlantis / Master of the Pearl Trident.
/// - <b>Cycling — Pay 2 life</b> (CR 702.29) — modelled as a structural
///   <see cref="KeywordAbility"/> marker ("Cycling—Pay 2 life"). There is
///   no <c>CyclingAlternativeCost</c> in the engine yet (see
///   <c>Majik.Core/Costs/</c> — Flashback, Evoke, Spectacle, Madness,
///   Suspend, etc. are present; Cycling is not). The marker is the same
///   "shape-first, mechanics later" posture used for Kicker on Burst
///   Lightning, Overload stubs, and similar not-yet-plumbed keywords.
///   Wire a real <c>CyclingAlternativeCost</c> (life-payment variant)
///   when the cycling infrastructure lands.
///
/// ## Why Street Wraith matters in Modern
/// The life-cycling ability makes Street Wraith a "free" cantrip — discard
/// it for 2 life, draw a card. This is used in Death's Shadow, Living End,
/// and cascade decks to cycle through the deck without spending mana. The
/// v1 stub makes the card available in the factory registry so cascade /
/// graveyard / hand interactions function against the correct card identity;
/// the cycling activation itself is a no-op until the cycling framework ships.
///
/// ## Deferred (v1 gaps)
/// - <b>Combat-validator Swampwalk enforcement</b>: Street Wraith can't be
///   blocked as long as the defending player controls a Swamp (CR 702.14b).
///   The <see cref="KeywordAbility"/> marker ensures keyword-detection logic
///   sees "Swampwalk", but the combat blocker gate in the rules engine does
///   not yet check landwalk keywords. Same gap as Islandwalk.
/// - <b>Cycling activation</b>: "Pay 2 life, Discard this card: draw a card"
///   is not implemented. A <c>CyclingAlternativeCost</c> with a life-payment
///   variant is the correct long-term shape. v1 just registers the keyword
///   marker so the ability string is visible on the card.
/// </summary>
public static class StreetWraithFactory
{
    public const string CardName = "Street Wraith";
    public const string PrintedManaCost = "{3}{B}{B}";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Street Wraith. No runtime services are needed for v1 —
    /// both abilities are keyword markers only.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Wraith });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.14 — Swampwalk. KeywordAbility marker; combat-validator
        // enforcement (can't be blocked if defending player controls a Swamp)
        // is deferred — same posture as Islandwalk on Lord of Atlantis.
        card.AddAbility(new KeywordAbility("Swampwalk", card, owner));

        // CR 702.29 — Cycling — Pay 2 life. Structural keyword marker only
        // in v1; no CyclingAlternativeCost exists yet. Same stub posture as
        // Kicker on Burst Lightning. The marker preserves the ability string
        // on the card for display and keyword-detection purposes.
        card.AddAbility(new KeywordAbility("Cycling—Pay 2 life", card, owner));

        return card;
    }
}
