using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Doomed Dissenter (Amonkhet, {1}{B}).
///
/// Creature — Human 1/1. Oracle text:
///   "When this creature dies, create a 2/2 black Zombie creature token."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human, mana cost {1}{B}, mana value 2, black.
/// - <b>Dies trigger</b> (CR 603.6c / 700.4): fires when Doomed Dissenter
///   moves Battlefield → Graveyard. On resolve, creates one 2/2 black Zombie
///   creature token for the controller via <see cref="TokenFactory"/>
///   (CR 111 / 111.4). The token has no keyword abilities.
/// - No keyword abilities on the card itself.
///
/// ## Wiring
/// - <see cref="Create(Player)"/> attaches the dies trigger to the card shape
///   without registering with a <see cref="TriggerManager"/>. Suitable for
///   shape / dispatcher tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> additionally
///   registers the dies trigger and threads the optional
///   <see cref="ZoneService"/> into token ETB so that
///   <see cref="Majik.Core.Events.CardMovedEvent"/> fires when the token
///   enters the battlefield (mirrors Doomed Traveler / Aven Fisher).
///
/// ## Active zones
/// The dies trigger includes <see cref="ZoneType.Graveyard"/> in its active
/// zones because <see cref="ZoneService"/> stamps <c>card.Zone = Graveyard</c>
/// before publishing the <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
/// — the trigger must still be observable at evaluation time (same posture
/// as Doomed Traveler, Aven Fisher, Wurmcoil Engine, Voice of Resurgence).
///
/// ## Rules reference
/// - CR 603.6c — when a triggered ability's condition is met, it triggers.
/// - CR 700.4 — "dies" means moved from the battlefield to the graveyard.
/// - CR 111 / 111.4 — tokens are created on the battlefield under the
///   controller's control.
/// </summary>
[CardName("Doomed Dissenter")]
public static class DoomedDissenterFactory
{
    public const string CardName = "Doomed Dissenter";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    private static readonly TokenFactory.TokenSpec ZombieTokenSpec = new(
        Name: "Zombie",
        Power: 2,
        Toughness: 2,
        Subtypes: new[] { CardSubtype.Zombie },
        Keywords: null,
        // CR 105 / CR 111.4 — "2/2 black Zombie creature token".
        Colors: new[] { ManaColor.Black });

    /// <summary>
    /// Construct Doomed Dissenter with the dies trigger attached to the card
    /// shape but NOT registered with a <see cref="TriggerManager"/>. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Doomed Dissenter with optional <see cref="TriggerManager"/>
    /// and <see cref="ZoneService"/> wiring. When <paramref name="triggers"/>
    /// is supplied, the dies trigger is registered so a Battlefield → Graveyard
    /// <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> places it
    /// on the stack automatically. When <paramref name="zoneService"/> is
    /// supplied, the token ETB publishes a <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// so ETB observers (e.g. Soul Warden) see the token enter.
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
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Human });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.6c / 700.4 — "When this creature dies, create a 2/2 black
        // Zombie creature token."
        // "Dies" = Battlefield → Graveyard (CR 700.4). Active zones include
        // Graveyard so the trigger is still observable after ZoneService
        // stamps card.Zone = Graveyard before publishing the CardMovedEvent.
        var diesEffect = new Effect(
            $"{CardName} dies: create a 2/2 black Zombie creature token",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 111 / 111.4 — create one 2/2 black Zombie creature token
                // for the controller. No keywords on the token.
                TokenFactory.CreateOnBattlefield(ZombieTokenSpec, controller, zoneService);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // Battlefield + Graveyard: ZoneService stamps zone BEFORE the
            // CardMovedEvent fires, so the trigger must be active in both
            // zones to evaluate correctly (mirrors Doomed Traveler / Aven
            // Fisher / Wurmcoil Engine / Voice of Resurgence pattern).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
