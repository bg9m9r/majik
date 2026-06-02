using Majik.Core.Abilities;
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
/// Named-card factory for Bastion of Remembrance (Ikoria: Lair of
/// Behemoths, {2}{B}).
///
/// Enchantment. Oracle text (Scryfall, verified):
///   "When this enchantment enters, create a 1/1 white Human Soldier
///    creature token.
///    Whenever a creature you control dies, each opponent loses 1 life
///    and you gain 1 life."
///
/// Bastion is an enchantment-bodied aristocrat: it staples a Bitterblossom-
/// style ETB token onto a Cruel Celebrant / Zulaport Cutthroat death-drain.
///
/// ## Implemented (v1)
/// - Enchantment shape at {2}{B}; owner / controller wired.
/// - <b>ETB token trigger (CR 603.6e)</b>: a self-ETB
///   <see cref="TriggeredAbility"/> built from
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> fires when Bastion
///   itself enters the battlefield. On resolution it creates one 1/1
///   white Human Soldier creature token under the controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/>; white colour is
///   stamped explicitly (CR 111.4) since tokens have no mana cost.
/// - <b>Aristocrat death trigger (CR 603.1 + CR 700.4)</b>: a single
///   <see cref="TriggeredAbility"/> fires on <see cref="CardMovedEvent"/>
///   with FromZone = Battlefield + ToZone = Graveyard where the moved
///   card is a <see cref="CardType.Creature"/> controlled by Bastion's
///   controller. Unlike Cruel Celebrant the printed wording is plain
///   "a creature you control" — no planeswalker clause — so the predicate
///   filters on <see cref="CardType.Creature"/> only.
/// - <b>Drain effect</b>: on resolution each opponent loses 1 life and
///   the controller gains 1 life. Opponents are enumerated via the
///   optional <paramref name="opponentResolver"/> (mirrors Cruel
///   Celebrant — single-arg <c>Create(owner)</c> silently no-ops the
///   opponent-drain side; the lifegain side always fires). CR 119.3 —
///   the loss and the gain are two discrete life-change events, not a
///   single lifelink event.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only path. Both triggers are
///   attached but not registered with a <see cref="TriggerManager"/>;
///   tests fire them manually. Token creation falls back to raw zone
///   moves (no <see cref="ZoneService"/>) so token-ETB triggers won't
///   auto-fire.
/// - <see cref="Create(Player, Func{IReadOnlyList{Player}}?, IEventBus?, TriggerManager?, ZoneService?)"/>
///   — fully wired overload. Registers both triggers and threads
///   <see cref="ZoneService"/> into token creation so
///   <see cref="CardMovedEvent"/> fires when the Human Soldier enters.
///
/// ## Deferred (v1 gaps)
/// - <b>Last-known-information for the dying permanent</b>: CR 603.10 —
///   the moved card's controller must be read from LKI at the moment of
///   death. The engine currently keeps <see cref="Permanent.Controller"/>
///   on the card after the zone move, so this v1 implementation reads it
///   directly. Same posture as Cruel Celebrant / Blood Artist.
/// - <b>Self-death of the token does not re-trigger Bastion specially</b>:
///   the Human Soldier is a creature the controller owns, so its later
///   death feeds the drain trigger like any other controlled creature —
///   correct per the printed text.
/// </summary>
[CardName("Bastion of Remembrance")]
public static class BastionOfRemembranceFactory
{
    public const string CardName = "Bastion of Remembrance";
    public const string PrintedManaCost = "{2}{B}";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const int DrainAmount = 1;
    public const int GainAmount = 1;

    /// <summary>
    /// Construct Bastion of Remembrance with no live runtime services.
    /// Both triggered abilities are attached to the card shape but not
    /// registered with a <see cref="TriggerManager"/>, and no opponent
    /// resolver is wired (so the opponent-drain side is a no-op while the
    /// lifegain side still fires). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Bastion of Remembrance with optional runtime services.
    /// <paramref name="opponentResolver"/> supplies the player list the
    /// death-trigger drains 1 life from (typically every
    /// <c>Game.Players</c> entry that isn't the controller).
    /// <paramref name="triggers"/> registers both triggered abilities so
    /// the bus drives them automatically. <paramref name="zoneService"/>
    /// threads CardMovedEvent through the ETB token spawn.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB token trigger — CR 603.6e.
        //   "When this enchantment enters, create a 1/1 white Human
        //    Soldier creature token."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: create a 1/1 white Human Soldier creature token",
            () => CreateHumanSoldierToken(card.Controller ?? owner, zoneService));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Death trigger — CR 603.1 + CR 700.4.
        //   "Whenever a creature you control dies, each opponent loses 1
        //    life and you gain 1 life."
        // Plain "a creature you control" — creatures only (no planeswalker
        // clause, unlike Cruel Celebrant).
        // ----------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            return ReferenceEquals(e.Card.Controller, owner);
        });

        var drainEffect = new Effect(
            $"{CardName}: each opponent loses 1 life + controller gains 1 life",
            () =>
            {
                var opponents = opponentResolver?.Invoke();
                if (opponents != null)
                {
                    foreach (var opp in opponents)
                    {
                        if (ReferenceEquals(opp, owner)) continue;
                        opp.LoseLife(DrainAmount);
                    }
                }
                owner.GainLife(GainAmount);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 white Human Soldier creature
    /// token under <paramref name="controller"/>. White colour is stamped
    /// explicitly via <see cref="TokenFactory.TokenSpec.Colors"/> (tokens
    /// have no mana cost to derive colour from).
    /// </summary>
    public static Creature CreateHumanSoldierToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Human Soldier",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier },
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
