using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lonis, Cryptozoologist (Streets of New Capenna
/// Commander, {G}{U}).
///
/// Legendary Creature — Snake Elf Scout 2/2. Oracle text:
///   "Whenever another nontoken creature you control enters, investigate.
///    {T}, Sacrifice X Clues: Target opponent reveals the top X cards of
///    their library. You may put a nonland permanent card with mana value
///    X or less from among them onto the battlefield under your control.
///    That player puts the rest on the bottom of their library in a
///    random order."
///
/// ## Implemented (v1)
/// - 2/2 Legendary Creature — Snake Elf Scout, mana cost {G}{U}. Power +
///   toughness match the printed back-face (Lonis is printed as 2/2 in
///   the Commander Legends: Battle for Baldur's Gate sibling reprint as
///   well — values are stable).
/// - <b>Nontoken-creature-ETB investigate trigger (CR 603.1 / CR 701.30)</b>:
///   a <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>
///   that fires when the entering card lands on the battlefield, has
///   <see cref="CardType.Creature"/>, is NOT a token
///   (<see cref="Permanent.IsToken"/> false), is controlled by Lonis's
///   controller, and is NOT Lonis itself ("another" — CR 109.2). Effect:
///   <see cref="TokenFactory.CreateClue"/> creates a Clue artifact token
///   under Lonis's controller (CR 701.30).
///
/// ## Deferred (v1 gaps)
/// - <b>Activated ability "{T}, Sacrifice X Clues: opponent reveals top X
///   ..."</b>: requires variable-X activated-ability cost wiring (X picked
///   by the activator at announcement, then X individual Clue sacrifices),
///   a reveal-N-cards-from-library primitive, a "may put a nonland
///   permanent with mv ≤ X onto the battlefield under your control"
///   choose-and-cheat primitive, and a "remaining cards to bottom of
///   library in a random order" terminator (CR 701.20 random-bottom,
///   distinct from a full shuffle). None of those four primitives are in
///   the corpus yet at the activated-ability surface — closest siblings
///   (Tutor's reveal-top-N, Charbelcher's random-bottom) work at the
///   spell-resolve layer rather than from an activated cost. Surface the
///   activated half once a second card needs the same plumbing
///   (the obvious match is Eligeth, Crossroads Augur's library-look
///   variant — not yet shipped either).
/// </summary>
[CardName("Lonis, Cryptozoologist")]
public static class LonisCryptozoologistFactory
{
    public const string CardName = "Lonis, Cryptozoologist";
    public const string PrintedManaCost = "{G}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Lonis with no live trigger-manager / ZoneService wiring.
    /// The investigate trigger is attached to the card for shape
    /// observability but is not registered. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Lonis with optional <see cref="TriggerManager"/> +
    /// <see cref="ZoneService"/>. When <paramref name="triggers"/> is
    /// supplied the nontoken-creature-ETB investigate trigger is
    /// registered so the bus surfaces it as pending on a matching
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Snake, CardSubtype.Elf, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Nontoken-creature-ETB investigate trigger — CR 603.1 / CR 701.30.
        //   "Whenever another nontoken creature you control enters,
        //    investigate."
        // Predicate gates on:
        //   - ToZone == Battlefield (ETB),
        //   - Card.HasType(Creature) (CR 302.1),
        //   - NOT a token (Permanent.IsToken == false; per CR 111.5 every
        //     token is a Permanent, so non-Permanent cards trivially pass
        //     this leg too),
        //   - Card.Controller == Lonis's controller (CR 603.1 "you"),
        //   - Card != Lonis itself ("another" — CR 109.2 / CR 603.6e:
        //     Lonis's own ETB doesn't trigger her own ability).
        // ----------------------------------------------------------------
        var investigateCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Creature)
            && !(e.Card is Permanent perm && perm.IsToken)
            && ReferenceEquals(e.Card.Controller, owner)
            && !ReferenceEquals(e.Card, card));

        var investigateEffect = new Effect(
            $"{CardName}: investigate (create a Clue) — another nontoken creature you control entered",
            () =>
            {
                // CR 701.30 — Clue token under Lonis's controller.
                var controller = card.Controller ?? owner;
                TokenFactory.CreateClue(controller, zoneService);
            });

        var investigateTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: investigateCondition,
            effects: new IEffect[] { investigateEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(investigateTrigger);
        triggers?.RegisterTriggeredAbility(investigateTrigger);

        return card;
    }
}
