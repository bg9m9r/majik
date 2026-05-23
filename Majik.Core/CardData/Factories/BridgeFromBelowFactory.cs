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
/// Named-card factory for Bridge from Below (Future Sight, {B}). Banned
/// in Modern.
///
/// Enchantment — {B}. Oracle text:
///   "Whenever a nontoken creature is put into your graveyard from the
///    battlefield, if Bridge from Below is in your graveyard, create a
///    2/2 black Zombie creature token.
///    When a creature is put into an opponent's graveyard from the
///    battlefield, exile Bridge from Below."
///
/// ## Implemented (v1)
///
/// Two triggered abilities, both active while Bridge from Below sits in
/// its owner's graveyard (CR 603.6d — abilities of cards in the
/// graveyard that reference "is in your graveyard" or trigger on
/// graveyard-resident events). Both triggers register
/// <c>activeZones = {Graveyard}</c> so they fire while Bridge is in the
/// graveyard, mirroring the <see cref="WurmcoilEngineFactory"/>
/// activeZones pattern that lets the dies trigger see the source after
/// it has been moved.
///
/// - <b>Zombie-token trigger</b>: fires on
///   <see cref="CardMovedEvent"/> with FromZone = Battlefield + ToZone =
///   Graveyard when the moved card is a <see cref="CardType.Creature"/>,
///   is NOT a token (CR 111.3 — Bridge's printed text specifies
///   "nontoken creature"), and lands in the Bridge controller's
///   graveyard. The intervening-if reads "Bridge is in your graveyard"
///   at trigger-evaluation time (CR 603.4) — when Bridge has been
///   exiled it does not fire. Effect creates a 2/2 black Zombie
///   creature token under Bridge's controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/>.
/// - <b>Self-exile trigger</b>: fires on
///   <see cref="CardMovedEvent"/> Battlefield → Graveyard where the
///   moved card is a <see cref="CardType.Creature"/> landing in an
///   OPPONENT's graveyard (i.e. not Bridge's controller's). Effect
///   moves Bridge from its controller's graveyard to exile via raw
///   zone mutation (Bridge exits the graveyard, so the Zombie-token
///   trigger above cannot fire for the same event — CR 603.3 evaluates
///   triggers as a batch, then resolves in APNAP; the self-exile
///   trigger landing first leaves the Zombie trigger without a Bridge
///   to gate on, matching the printed "exile Bridge" intent).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Token-creature colour identity</b>: tokens carry subtype +
///   keywords but no explicit colour today (same scope decision as
///   Crashing Footfalls' "green" Rhinos, Wurmcoil's "colorless" Wurms).
///   Bridge's 2/2 black Zombie tokens are documented as black; the
///   runtime token has no colour stamp.
/// - <b>APNAP simultaneous-trigger ordering</b>: when one creature dies
///   to a chained event (combat damage, board wipe), CR 603.3b sorts
///   pending triggers by APNAP and within each player by the player's
///   choice. v1 fires triggers in registration order via
///   <see cref="TriggerManager"/>; "exile Bridge" landing before
///   "create Zombie" is a faithful default but not the only legal
///   ordering when multiple Bridges + multiple deaths interact.
///
/// CR rule references: 603.1 (triggered abilities), 603.6d (abilities
/// of cards in non-battlefield zones), 603.4 (intervening-if), 111
/// (tokens), 700.4 (dying = Battlefield → Graveyard).
/// </summary>
public static class BridgeFromBelowFactory
{
    public const string CardName = "Bridge from Below";
    public const string PrintedManaCost = "{B}";

    /// <summary>
    /// Construct Bridge from Below with no live runtime services. Both
    /// triggered abilities are attached to the card's
    /// <see cref="Card.Abilities"/> collection so structural / shape
    /// tests can observe them; for end-to-end bus-driven firing pass a
    /// live <see cref="TriggerManager"/> + <see cref="ZoneService"/>
    /// via the runtime overload.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Bridge from Below with optional runtime services.
    /// <paramref name="zoneService"/> is passed to
    /// <see cref="TokenFactory.CreateOnBattlefield"/> so the spawned
    /// Zombie tokens publish a <see cref="CardMovedEvent"/> when they
    /// enter; <paramref name="triggers"/> registers both triggered
    /// abilities so the bus drives them automatically.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trigger 1 — "Whenever a nontoken creature is put into your
        // graveyard from the battlefield, if Bridge from Below is in
        // your graveyard, create a 2/2 black Zombie creature token."
        // CR 603.1 + CR 603.4 (intervening-if).
        // ----------------------------------------------------------------
        var zombieCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            // Battlefield → Graveyard
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;

            // Creature, nontoken
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (e.Card is Permanent perm && perm.IsToken) return false;

            // Into BRIDGE controller's graveyard — "your graveyard" in
            // the printed text references Bridge's controller.
            return ReferenceEquals(e.Card.Owner, owner)
                || ReferenceEquals(e.Card.Controller, owner);
        });

        var zombieEffect = new Effect(
            "Bridge from Below: create a 2/2 black Zombie creature token",
            () => CreateZombieToken(owner, zoneService));

        var zombieTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: zombieCondition,
            effects: new IEffect[] { zombieEffect },
            // CR 603.4 — intervening-if: only fire if Bridge is in its
            // controller's graveyard at trigger evaluation time.
            interveningIf: () => card.Zone == ZoneType.Graveyard
                                 && owner.Zones.Graveyard.GetCards().Contains(card),
            // CR 603.6d — trigger is active while Bridge is in the
            // graveyard, not on the battlefield.
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(zombieTrigger);
        triggers?.RegisterTriggeredAbility(zombieTrigger);

        // ----------------------------------------------------------------
        // Trigger 2 — "When a creature is put into an opponent's
        // graveyard from the battlefield, exile Bridge from Below."
        // Note: printed text omits "nontoken" — tokens also fire this
        // (CR 111.4 — tokens that move zones cease to exist as state-
        // based actions, but the move event still publishes before SBAs
        // cull the token).
        // ----------------------------------------------------------------
        var exileCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;

            // Into an OPPONENT's graveyard — owner is not Bridge's
            // controller.
            return !ReferenceEquals(e.Card.Owner, owner)
                && !ReferenceEquals(e.Card.Controller, owner);
        });

        var exileEffect = new Effect(
            "Bridge from Below: exile this from controller's graveyard",
            () =>
            {
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;
                owner.Zones.Graveyard.RemoveCard(card);
                owner.Zones.Exile.AddCard(card);
                card.SetZone(ZoneType.Exile);
            });

        var exileTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: exileCondition,
            effects: new IEffect[] { exileEffect },
            // Only meaningful while Bridge is in the graveyard.
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(exileTrigger);
        triggers?.RegisterTriggeredAbility(exileTrigger);

        return card;
    }

    /// <summary>
    /// Create a 2/2 black Zombie creature token under
    /// <paramref name="controller"/>. Routes through
    /// <see cref="TokenFactory.CreateOnBattlefield"/> so the token
    /// publishes a <see cref="CardMovedEvent"/> when a live
    /// <see cref="ZoneService"/> is threaded in (downstream ETB
    /// listeners — Soul Warden, Cult of the Waxing Moon — fire).
    /// Colour identity ("black") is documented but the runtime token
    /// has no colour stamp (same gap as Crashing Footfalls / Pact of
    /// the Titan / Wurmcoil Engine).
    /// </summary>
    private static Creature CreateZombieToken(Player controller, ZoneService? zoneService)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Zombie",
            Power: 2,
            Toughness: 2,
            Subtypes: new[] { CardSubtype.Zombie });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
