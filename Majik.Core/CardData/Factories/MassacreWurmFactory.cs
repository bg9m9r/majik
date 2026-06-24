using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Massacre Wurm (New Phyrexia, {3}{B}{B}{B}).
/// Creature — Phyrexian Wurm 6/5. Oracle text (verified against Scryfall):
///   "When this creature enters, creatures your opponents control get -2/-2
///    until end of turn.
///    Whenever a creature an opponent controls dies, that player loses 2
///    life."
///
/// The base shape (name, Creature, Phyrexian + Wurm subtypes, {3}{B}{B}{B},
/// 6/5) is materialised from the embedded JSON definition
/// (<c>massacre-wurm.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two triggered abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// an ETB mass-pump sweep or a per-opponent-creature-death drain (same posture
/// as <see cref="TheMeathookMassacreFactory"/>, the structural analogue).
///
/// ## Implemented (v1)
/// - <b>ETB sweep</b> (CR 603.6a): on entering the battlefield, registers a
///   fixed <c>(-2, -2)</c> <see cref="PumpUntilEndOfTurnEffect"/> on every
///   creature an OPPONENT controls — read from the LIVE resolution context
///   (<c>rc.Game.AllPlayers</c> minus the controller), mirroring The Meathook
///   Massacre's all-players sweep but restricted to opponents and at a fixed
///   −2/−2 (no <c>PendingCastX</c>; the magnitude is printed, not chosen). Each
///   bonus walks the standard Layer-7c pipeline (CR 613) and expires at end of
///   turn (CR 514.2). Falls back to a no-op when no live game context is
///   available (shape path) — there is no "opponents" without a game.
/// - <b>Opponent-creature dies trigger</b> (CR 603.1 + CR 700.4): fires on a
///   <see cref="CardMovedEvent"/> Battlefield → Graveyard for any
///   <see cref="CardType.Creature"/> whose controller at the instant of death
///   is NOT this card's controller. CR 603.10 — the controller is read from the
///   last-known-information snapshot (<see cref="CardMovedEvent.LkiController"/>)
///   captured BEFORE the battlefield-exit controller reset (CR 110.2), so a
///   creature an opponent stole from you (Act of Treason) that dies still
///   counts as "a creature an opponent controls". Effect: <b>that player</b>
///   (the dying creature's controller — the opponent, per the printed
///   "that player") loses 2 life via <see cref="Player.LoseLife"/>.
///
/// ## Notes
/// - Unlike The Meathook Massacre the ETB sweep needs no <c>PendingCastX</c>
///   stamp: the −2/−2 is a printed constant, so blink / token-copy re-entries
///   re-apply the full sweep (correct — each ETB is its own instance of the
///   triggered ability, CR 603.6a).
/// - The drain victim is the dying creature's controller, not this card's
///   controller, so it is read directly off <c>e.LkiController</c> rather than
///   through <see cref="ContextOpponents"/>.
/// </summary>
[CardName("Massacre Wurm")]
public static class MassacreWurmFactory
{
    public const string CardName = "Massacre Wurm";
    public const string Slug = "massacre-wurm";

    /// <summary>
    /// Construct Massacre Wurm with no live runtime wiring (the dispatcher /
    /// shape path). Both triggered abilities are attached for shape
    /// observability; neither is registered with a <see cref="TriggerManager"/>.
    /// The ETB sweep is a no-op without a live game context (no "opponents"),
    /// and the dies-drain reads its victim off the event's LKI snapshot. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Massacre Wurm with optional runtime services.
    /// <paramref name="triggers"/> registers both triggered abilities so the bus
    /// drives them automatically. The ETB sweep reads every opponent's
    /// battlefield off the live resolution context at resolution
    /// (<c>rc.Game.AllPlayers</c>), so it is correct on the production routed
    /// build.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Phyrexian + Wurm, {3}{B}{B}{B}, 6/5). No abilities in the JSON — the
        // two triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB sweep — CR 603.6a.
        //   "When this creature enters, creatures your opponents control get
        //    -2/-2 until end of turn."
        // For each creature an opponent controls (live context, AllPlayers
        // minus controller), register a fixed -2/-2 PumpUntilEndOfTurnEffect on
        // its own ActiveEffects service so the bonus walks the Layer-7c pipeline
        // (CR 613) and expires at end of turn (CR 514.2). No PendingCastX — the
        // magnitude is a printed constant.
        // ----------------------------------------------------------------
        var etbSweepEffect = new Effect(
            "Massacre Wurm — creatures your opponents control get -2/-2 until end of turn",
            rc =>
            {
                var controller = card.Controller ?? owner;
                var players = rc.Game?.AllPlayers;
                if (players == null) return ValueTask.CompletedTask; // shape path — no opponents

                foreach (var p in players)
                {
                    // "creatures your opponents control" — skip the controller's
                    // own creatures (CR 102.1).
                    if (ReferenceEquals(p, controller)) continue;

                    foreach (var c in p.Zones.Battlefield.GetCards().OfType<Creature>())
                    {
                        c.ActiveEffects?.Register(new PumpUntilEndOfTurnEffect(c, -2, -2));
                    }
                }
                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbSweepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Opponent-creature dies trigger — CR 603.1 + CR 700.4.
        //   "Whenever a creature an opponent controls dies, that player loses
        //    2 life."
        // Fires on CardMovedEvent Battlefield → Graveyard for any Creature whose
        // controller (LKI, CR 603.10) is NOT this card's controller. "That
        // player" = the dying creature's controller (the opponent), so the
        // life-loss victim is read off e.LkiController.
        // ----------------------------------------------------------------
        var oppDiesCondition = new EventTriggerCondition<CardMovedEvent>((e, ability) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            // CR 603.10 — controller read from last-known information at the
            // instant of death (e.LkiController), NOT the post-reset live card.
            if (ReferenceEquals(e.LkiController, card.Controller ?? owner)) return false;

            // CR 603.3 — stamp "that player" (the dying creature's controller,
            // i.e. the opponent) so the untargeted drain reads it at resolution
            // off ResolutionContext.TriggeringPlayer (same pattern as
            // CardDefRuntime's sacrifice triggers).
            if (ability is TriggeredAbility ta)
            {
                ta.SetTriggeringPlayer(e.LkiController);
            }
            return true;
        });

        var oppDiesEffect = new Effect(
            "Massacre Wurm — that player loses 2 life",
            ctx =>
            {
                // "that player" = the dying creature's controller, stamped onto
                // the ability by the condition (CR 603.3) and read off the live
                // resolution context.
                ctx.TriggeringPlayer?.LoseLife(2);
                return ValueTask.CompletedTask;
            });

        var oppDiesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: oppDiesCondition,
            effects: new IEffect[] { oppDiesEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(oppDiesTrigger);
        triggers?.RegisterTriggeredAbility(oppDiesTrigger);

        return card;
    }
}
