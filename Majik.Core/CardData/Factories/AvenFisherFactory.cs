using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aven Fisher (Odyssey / Magic 2013, {3}{U}).
///
/// Creature — Bird Soldier 2/2. Oracle text:
///   "Flying. When this creature dies, you may draw a card."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Bird Soldier, mana cost {3}{U}, mana value 4.
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="KeywordAbility"/>
///   marker; read by the combat-abilities evasion subsystem.
/// - <b>Dies trigger</b> (CR 603.6c / 700.4): fires when Aven Fisher moves
///   Battlefield → Graveyard. On resolve, the controller draws one card via
///   <see cref="Fx.DrawCards"/> (CR 121.1). The "you may" is auto-accepted
///   in v1 (draw is unconditional) — consistent with other "you may draw"
///   implementations across the factory family.
///
/// ## Wiring
/// - <see cref="Create(Player)"/> attaches the Flying keyword and the dies
///   trigger to the card shape without registering with a
///   <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, TriggerManager?)"/> additionally registers
///   the dies trigger with the live <see cref="TriggerManager"/> so a
///   Battlefield → Graveyard <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
///   places it on the stack automatically (mirrors StitchersSupplierFactory's
///   two-arg pattern).
///
/// ## Active zones
/// The dies trigger includes <see cref="ZoneType.Graveyard"/> in its active
/// zones because <see cref="ZoneService"/> stamps <c>card.Zone = Graveyard</c>
/// before publishing the <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
/// — the trigger must still be observable at evaluation time (same posture
/// as Stitcher's Supplier, Young Wolf / Undying, Voice of Resurgence).
///
/// ## Deferred (v1)
/// - "You may" decision: v1 auto-accepts (draws unconditionally). A future
///   pass can wire a player-decision hook for true optional draw.
/// </summary>
[CardName("Aven Fisher")]
public static class AvenFisherFactory
{
    public const string CardName = "Aven Fisher";
    public const string PrintedManaCost = "{3}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Aven Fisher with the Flying keyword and dies trigger
    /// attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Aven Fisher with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the dies
    /// trigger is registered so a Battlefield → Graveyard
    /// <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> places
    /// it on the stack automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Bird, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker. Read by the combat-abilities
        // subsystem for evasion enforcement (blockers must have Flying or
        // Reach per CR 509.1b).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 603.6c / 700.4 — "When this creature dies, you may draw a card."
        // "Dies" = Battlefield → Graveyard (CR 700.4). Active zones include
        // Graveyard so the trigger is still observable after ZoneService
        // stamps card.Zone = Graveyard before publishing the CardMovedEvent.
        var diesEffect = new Effect(
            $"{CardName} dies: draw a card (you may — v1 auto-accepts)",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 121.1 — "draw a card". Routes through Fx.DrawCards so
                // any active draw-replacement effect (e.g. Dredge) can
                // intercept it (CR 614).
                Fx.DrawCards(controller, 1);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // Battlefield + Graveyard: ZoneService stamps zone BEFORE the
            // CardMovedEvent fires, so the trigger must be active in both
            // zones to evaluate correctly (mirrors Stitcher's Supplier /
            // Young Wolf / Voice of Resurgence).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
