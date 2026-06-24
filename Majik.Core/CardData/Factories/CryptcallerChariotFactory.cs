using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cryptcaller Chariot (Duskmourn: House of Horror,
/// {3}{B}).
///
/// Artifact — Vehicle 5/5. Oracle text (Scryfall, verified):
///   "Menace
///    Whenever you discard one or more cards, create that many tapped 2/2
///    black Zombie creature tokens.
///    Crew 2"
///
/// A black graveyard / discard payoff: every discard mints an equal number
/// of tapped Zombie bodies, then those bodies can crew the chariot.
///
/// ## Shape source
/// Card identity (name, {3}{B}, 5/5, Artifact + Creature shell, Vehicle
/// subtype, Menace keyword) is loaded from
/// <c>Majik.Core/CardData/Cards/cryptcaller-chariot.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/> (same JSON-driven Vehicle
/// shape as Cultivator's Caravan). The discard trigger is attached in code
/// below — the JSON ability schema does not express a discard-linked token
/// trigger.
///
/// ## Implemented (v1)
///
/// - <b>5/5 Artifact — Vehicle at {3}{B} with Menace.</b> The Vehicle shell
///   is a <see cref="Creature"/> with <see cref="CardType.Artifact"/>
///   additively stamped (CR 301.1 / 302.1 — the "Artifact Vehicle" pattern,
///   so <c>BasePower</c>/<c>BaseToughness</c> = 5/5 flow through
///   <see cref="CardData.Vehicles.CrewAction"/>). Menace (CR 702.111) is a
///   <see cref="KeywordAbility"/> marker minted by the JSON loader and read
///   by <see cref="Combat.CombatAbilities.HasMenace"/> once the vehicle is
///   crewed into a creature.
///
/// - <b>Discard trigger (CR 603.1).</b> "Whenever you discard one or more
///   cards, create that many tapped 2/2 black Zombie creature tokens." The
///   engine has no dedicated <c>DiscardedEvent</c> (see
///   <see cref="MaraudingMakoFactory"/> / <see cref="ContainmentConstructFactory"/>);
///   discards funnel through <see cref="CardMovedEvent"/> with
///   <c>FromZone == Hand &amp;&amp; ToZone == Graveyard</c>, one event PER
///   card. The trigger filters that funnel to cards owned by the chariot's
///   controller ("you discard" — CR 109.5) and, on each matching event,
///   mints one tapped 2/2 black Zombie token via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>
///   (CR 111 / CR 111.6).
///
///   <para><b>"That many" via per-card funnelling.</b> Because each
///   discarded card publishes its own <see cref="CardMovedEvent"/>, a batch
///   discard of N cards fires the trigger N times, minting N tokens total —
///   the observable end state matches the printed "create that many"
///   (CR 701.8 — discarding multiple cards). Same v1 acceptable-shape
///   envelope as Marauding Mako's per-card counter placement.</para>
///
///   <para><b>No nonland gate.</b> CR 701.8 "discard one or more cards" is
///   type-agnostic — lands count. The filter therefore does not exclude
///   <see cref="CardType.Land"/>.</para>
///
///   <para><b>Tapped.</b> Each minted Zombie is tapped on entry (CR 111.6 —
///   "create that many tapped … tokens") via <see cref="Permanent.Tap()"/>.</para>
///
/// - <b>Crew 2</b> (CR 702.122): surfaced via <see cref="CrewCost"/> so
///   callers route through <see cref="CardData.Vehicles.CrewAction.Crew"/>
///   (same structural-data shape as <see cref="EsikasChariotFactory"/> — no
///   activated-ability surface yet; the engine's <c>CrewAction</c> is
///   invoked directly by tests / bots).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Discard trigger
///   attached as a marker; no event-bus subscription, so no tokens mint.
///   Suitable for dispatcher / shape / crew tests.
/// - <see cref="Create(Player, IEventBus?, ZoneService?)"/> — fully wired.
///   The discard watcher subscribes so the "you discard one or more cards →
///   that many tapped Zombies" loop runs end to end; tokens route through
///   the optional <see cref="ZoneService"/> so <see cref="CardMovedEvent"/>
///   fires for downstream ETB listeners.
///
/// CR rule references: 301.1 / 302.1 (Artifact Vehicle multi-type),
/// 603.1 (trigger), 701.8 (discard), 109.5 ("you"), 111 / 111.6 (tokens,
/// tapped), 702.111 (Menace), 702.122 (Crew).
/// </summary>
[CardName("Cryptcaller Chariot")]
public static class CryptcallerChariotFactory
{
    public const string CardName = "Cryptcaller Chariot";

    /// <summary>Crew cost (CR 702.122) — total tapped power ≥ 2 crews it.</summary>
    public const int CrewCost = 2;

    /// <summary>Vehicle base power, shipped through VehicleCrewEffect once crewed.</summary>
    public const int VehiclePower = 5;

    /// <summary>Vehicle base toughness, shipped through VehicleCrewEffect once crewed.</summary>
    public const int VehicleToughness = 5;

    /// <summary>Each discarded card mints one tapped 2/2 black Zombie (CR 603.1).</summary>
    public const int ZombiePower = 2;
    public const int ZombieToughness = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("cryptcaller-chariot");

    /// <summary>
    /// Construct Cryptcaller Chariot with no live event-bus wiring (the
    /// shape / dispatcher path). The discard trigger is attached as a marker
    /// but its watcher is NOT subscribed, so no Zombie tokens mint. Suitable
    /// for factory-shape / dispatcher / crew tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, zoneService: null);

    /// <summary>
    /// Construct Cryptcaller Chariot. When <paramref name="eventBus"/> is
    /// supplied the discard watcher subscribes so the "you discard one or
    /// more cards → that many tapped 2/2 black Zombie tokens" loop runs end
    /// to end. When <paramref name="zoneService"/> is supplied, minted
    /// tokens route through it so each publishes <see cref="CardMovedEvent"/>
    /// on battlefield entry (downstream ETB listeners fire).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Discard trigger — CR 603.1.
        //   "Whenever you discard one or more cards, create that many tapped
        //    2/2 black Zombie creature tokens."
        //
        // The engine has no dedicated DiscardedEvent; discards funnel
        // through CardMovedEvent with FromZone == Hand && ToZone ==
        // Graveyard, one event PER card (see MaraudingMakoFactory). Each
        // matching event mints one tapped 2/2 black Zombie; a batch discard
        // of N cards thus mints N tokens ("that many"). No nonland gate —
        // CR 701.8 "discard one or more cards" counts every card type.
        // ----------------------------------------------------------------
        bool IsControllerDiscard(CardMovedEvent e)
        {
            if (e.FromZone != ZoneType.Hand) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            // "You discard" — gate to the chariot's controller (CR 109.5).
            // The discarded card's owner is the discarder.
            return ReferenceEquals(e.Card.Owner, card.Controller ?? owner);
        }

        // Marker triggered ability so factory-shape / dispatch tests can
        // assert the discard trigger is attached. Actual token minting is
        // driven by the event-bus subscription below.
        var triggerMarker = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) => IsControllerDiscard(e)),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: create a tapped 2/2 black Zombie (you discarded a card)",
                    () => CreateTappedZombie(card.Controller ?? owner, zoneService)),
            },
            // CR 113.6 — abilities on permanent cards function from the
            // battlefield only. A chariot in hand / graveyard does not fire.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(triggerMarker);

        if (eventBus != null)
        {
            eventBus.Subscribe<CardMovedEvent>(e =>
            {
                if (!IsControllerDiscard(e)) return;
                // CR 113.6 — only fire while the chariot is on the
                // battlefield (the discarded card itself, in hand→graveyard
                // transit, is not on the battlefield).
                if (card.Zone != ZoneType.Battlefield) return;
                CreateTappedZombie(card.Controller ?? owner, zoneService);
            });
        }

        return card;
    }

    /// <summary>
    /// CR 603.1 / CR 111 — mint one tapped 2/2 black Zombie creature token
    /// under <paramref name="controller"/>'s control. CR 111.6 — the token
    /// enters tapped, so <see cref="Permanent.Tap()"/> is applied after the
    /// untapped battlefield entry. Black colour (CR 105) is stamped via
    /// <see cref="TokenFactory.TokenSpec.Colors"/>.
    /// </summary>
    private static Creature CreateTappedZombie(Player controller, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Zombie",
            Power: ZombiePower,
            Toughness: ZombieToughness,
            Subtypes: new[] { CardSubtype.Zombie },
            Colors: new[] { ManaColor.Black });

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);
        token.Tap();
        return token;
    }
}
