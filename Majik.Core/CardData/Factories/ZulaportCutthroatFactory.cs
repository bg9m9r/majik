using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Zulaport Cutthroat (Battle for Zendikar,
/// {1}{B}).
///
/// Creature — Human Rogue 1/1. Oracle text (Scryfall, verified):
///   "Whenever Zulaport Cutthroat or another creature you control dies,
///    each opponent loses 1 life and you gain 1 life."
///
/// Zulaport Cutthroat is the controller-gated cousin of Blood Artist —
/// only deaths of creatures Cutthroat's controller controls trigger,
/// but the drain hits EVERY opponent (multiplayer scaling). Pairs
/// with Blood Artist + Falkenrath Noble as the Death-Drain Cycle.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Rogue at {1}{B}, owner/controller wired.
/// - <b>Death trigger</b> (CR 603.1 + CR 700.4): a single
///   <see cref="TriggeredAbility"/> fires on
///   <see cref="CardMovedEvent"/> with FromZone = Battlefield + ToZone =
///   Graveyard where (a) the moved card has <see cref="CardType.Creature"/>
///   AND (b) its controller is the Cutthroat's controller. The printed
///   "Zulaport Cutthroat or another creature you control" wording
///   collapses to "a creature you control" because Cutthroat is itself
///   a creature its controller controls — same union collapse as
///   Cruel Celebrant.
/// - <b>Drain side</b>: on resolution each opponent loses 1 life and
///   the controller gains 1 life. Opponents are enumerated via the
///   optional <paramref name="opponentResolver"/> (mirrors The Meathook
///   Massacre / Cruel Celebrant resolver convention — single-arg
///   <c>Create(owner)</c> silently no-ops the opponent-drain side, the
///   lifegain side ALWAYS fires per the printed "and you gain 1 life"
///   clause).
///
/// ## Notes
/// - <b>Self-trigger</b>: Cutthroat's own death triggers its ability
///   (CR 603.6c — self-naming dies trigger reads LKI just before
///   leaving the battlefield; trigger resolves from the graveyard).
///   v1 keeps activeZones at Battlefield + Graveyard so the self-death
///   case still drains correctly.
/// - <b>Each-opponent semantics</b>: CR 102.4 — each opponent of the
///   Cutthroat's controller. In a 2-player game this is 1 player; in
///   multiplayer it scales. The resolver supplies the list (typically
///   all <c>Game.Players</c> minus controller).
/// - <b>Discrete life events</b>: CR 119.3 — lifegain and lifeloss are
///   separate events; matters for lifegain-payoff / life-loss-matters
///   triggers downstream.
/// - <b>Last-known-information for the dying creature's controller
///   (CR 603.10)</b>: the "a creature you control" gate reads the dying
///   creature's controller from <see cref="CardMovedEvent.LkiController"/>
///   — the snapshot captured at the instant of death by
///   <see cref="Majik.Core.Services.ZoneService"/>, BEFORE the
///   battlefield-exit controller reset (CR 110.2). A creature controlled
///   via a control-change effect that dies fires the correct player's
///   payoff.
/// </summary>
[CardName("Zulaport Cutthroat")]
public static class ZulaportCutthroatFactory
{
    public const string CardName = "Zulaport Cutthroat";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int DrainAmount = 1;
    public const int GainAmount = 1;

    /// <summary>
    /// Construct Zulaport Cutthroat. The death-trigger drains 1 life from
    /// each opponent (read from the LIVE resolution context at resolution via
    /// <see cref="ContextOpponents"/>) and gains the controller 1 life. On the
    /// production routed build (which dispatches this single-arg overload and
    /// auto-binds the trigger via <see cref="TriggerManager.BindCard"/>) the
    /// drain is correct — it is no longer inert.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Zulaport Cutthroat with optional runtime services.
    /// <paramref name="triggers"/> registers the triggered ability so the bus
    /// drives it automatically. "Each opponent" is always read from the live
    /// resolution context at resolution.
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Rogue, CardSubtype.Ally });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Death trigger — CR 603.1 + CR 700.4.
        //   "Whenever Zulaport Cutthroat or another creature you control
        //    dies, each opponent loses 1 life and you gain 1 life."
        // The "or another creature you control" union collapses to "a
        // creature you control" because Cutthroat is itself a creature
        // it controls.
        // ----------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            // CR 603.10 — read the dying creature's controller from last-known
            // information at the instant of death (e.LkiController), NOT off the
            // moved card. The engine resets Controller back to the owner on a
            // battlefield exit (CR 110.2), so the live read is stale for a
            // creature controlled via a control-change effect.
            return ReferenceEquals(e.LkiController, owner);
        });

        // "Each opponent" is read from the LIVE resolution context
        // (ctx.Game.AllPlayers, filtered to non-controller / not-lost) — NOT a
        // captured resolver, which was null on the routed prod build and made
        // the drain INERT in real games (mirrors Stormbreath #2540 / Yawgmoth +
        // Priest #2543 / Grist #2549). The lifegain side always fires.
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses 1 life + controller gains 1 life",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    opp.LoseLife(DrainAmount);
                }
                controller.GainLife(GainAmount);
                return ValueTask.CompletedTask;
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { drainEffect },
            // CR 603.6c — self-naming dies trigger must remain active in
            // the graveyard so Cutthroat's OWN death still resolves the
            // drain/gain. Same posture as Blood Artist / Falkenrath Noble.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
