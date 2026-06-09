using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sheoldred, the Apocalypse (Dominaria United, {2}{B}{B}).
///
/// Legendary Creature — Phyrexian Praetor 4/5. Oracle text:
///   "Deathtouch
///    Whenever you draw a card, you gain 2 life and each opponent loses 2 life."
///
/// ## Implemented (v1)
/// - 4/5 Legendary Creature — Phyrexian Praetor, mana cost {2}{B}{B}, owner /
///   controller wired.
/// - Deathtouch (CR 702.2) wired as a <see cref="KeywordAbility"/> marker;
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/> reads it.
/// - <b>Draw trigger (CR 603.1)</b>: "Whenever you draw a card, you gain 2
///   life and each opponent loses 2 life." Modelled as a single triggered
///   ability over <see cref="CardDrawnEvent"/> filtered to the controller
///   (<see cref="Triggers.OnCardDrawnByPlayer"/>). On resolution the
///   controller gains 2 life and every opponent supplied by the optional
///   <paramref name="opponentResolver"/> loses 2 life. The dispatcher path
///   (no resolver wired) still gains 2 life for the controller, while the
///   "each opponent loses 2" clause silently no-ops — matching the shape
///   convention used by Liliana of the Veil's player-list-resolver pattern.
///
/// ## Deferred (v1 gaps)
/// - <b>Live opponent enumeration without a resolver</b>: <c>Player</c>
///   doesn't expose an opponent list at construction time; the engine
///   resolves "each opponent" from <c>Game.Players</c> at runtime. The
///   factory takes an explicit resolver so tests + the engine wire-up site
///   can both feed in the right player set without depending on a global.
/// </summary>
[CardName("Sheoldred, the Apocalypse")]
public static class SheoldredTheApocalypseFactory
{
    /// <summary>
    /// Construct Sheoldred, the Apocalypse. The draw trigger gains 2 life for
    /// the controller and drains 2 from each opponent, read from the LIVE
    /// resolution context at resolution (<see cref="ContextOpponents"/>) — so
    /// the drain is correct on the production routed build (which dispatches
    /// this single-arg overload and auto-binds the trigger via
    /// <see cref="TriggerManager.BindCard"/>).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Sheoldred, the Apocalypse with an optional event bus +
    /// trigger manager. When <paramref name="triggers"/> is supplied, the
    /// trigger is registered so <see cref="CardDrawnEvent"/> for the controller
    /// places it on the stack automatically. "Each opponent" is always read
    /// from the live resolution context at resolution.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Sheoldred, the Apocalypse",
            manaCost: "{2}{B}{B}",
            power: 4,
            toughness: 5,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Praetor });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Deathtouch — CR 702.2. CombatAbilities.HasDeathtouch consumes
        // this marker in the combat damage / lethal-damage paths.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        // ----------------------------------------------------------------
        // Draw trigger — CR 603.1.
        //   "Whenever you draw a card, you gain 2 life and each opponent
        //    loses 2 life."
        // Triggers.OnCardDrawnByPlayer filters CardDrawnEvent to the
        // controller (the trigger does NOT fire for opponents' draws —
        // CR 603.1: "you" means the controller of the triggered ability).
        // Fires once per drawn card (multiple draws stack — CR 603.2c).
        // ----------------------------------------------------------------
        // "Each opponent" is read from the LIVE resolution context
        // (ctx.Game.AllPlayers, filtered to non-controller / not-lost) — NOT a
        // captured resolver. The production routed build dispatches the
        // single-arg shape build (resolver-null), so the prior resolver read
        // made the drain INERT in real games; reading the context fixes it
        // (mirrors Stormbreath #2540 / Yawgmoth + Priest #2543 / Grist #2549).
        var drainEffect = new Effect(
            "Sheoldred, the Apocalypse: you gain 2 life and each opponent loses 2 life",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(2);

                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    opp.LoseLife(2);
                }
                return ValueTask.CompletedTask;
            });

        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnCardDrawnByPlayer(owner),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drawTrigger);
        triggers?.RegisterTriggeredAbility(drawTrigger);

        return card;
    }
}
