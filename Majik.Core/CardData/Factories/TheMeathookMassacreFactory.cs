using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for The Meathook Massacre (Innistrad: Midnight Hunt,
/// {X}{B}{B}).
///
/// Legendary Enchantment. Oracle text:
///   "When The Meathook Massacre enters, all creatures get -X/-X until end
///    of turn.
///    Whenever a creature an opponent controls dies, you gain 1 life.
///    Whenever a creature you control dies, each opponent loses 1 life."
///
/// ## Implemented (v1)
/// - Legendary Enchantment {X}{B}{B} with owner/controller wired.
/// - <b>ETB sweep trigger</b> (CR 603.6a, CR 122.1g): on entering the
///   battlefield, registers a <see cref="PumpUntilEndOfTurnEffect"/>
///   with <c>(-X, -X)</c> on every creature on the battlefield via each
///   creature's own <see cref="Creature.ActiveEffects"/>. Mirrors
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Counters.CountersSpellFactory.AllCreaturesPumpSpell"/>'s
///   per-creature registration pattern but iterates every player's
///   battlefield (resolver-supplied; falls back to controller-only when
///   no resolver, same convention as Pernicious Deed). X is read from
///   <see cref="Card.PendingCastX"/>, stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> at cast time. The
///   ETB-stamp is consumed after the sweep so a re-entry (blink, copy)
///   leaves the new copy with no sweep (X=0), matching the printed
///   "as it enters" semantics — same shape as Chalice of the Void's X
///   counter placement.
/// - <b>Opponent-creature dies trigger</b> (CR 603.1, CR 700.4): fires
///   on <see cref="CardMovedEvent"/> with FromZone = Battlefield + ToZone
///   = Graveyard when the moved card is a <see cref="CardType.Creature"/>
///   whose controller is NOT the Massacre's controller. Effect calls
///   <see cref="Player.GainLife"/> on the controller for 1.
/// - <b>Own-creature dies trigger</b> (CR 603.1, CR 700.4): fires on
///   <see cref="CardMovedEvent"/> Battlefield → Graveyard where the moved
///   card is a Creature whose controller IS the Massacre's controller.
///   Effect drains 1 life from every player supplied by an optional
///   <paramref name="opponentResolver"/> (skipping the controller itself
///   defensively). Mirrors Sheoldred, the Apocalypse's
///   <c>opponentResolver</c> shape — single-arg dispatcher path
///   silently no-ops the drain without a resolver.
///
/// ## Deferred (v1 gaps)
/// - <b>Strict CR 122.1g "enters with" timing for the sweep</b>: the
///   printed text reads as a triggered ability ("When ... enters") so
///   the ETB-on-the-stack model is faithful, but the X stamp lives on
///   the card and is consumed eagerly. Same shape as Chalice of the
///   Void.
///
/// ## Notes
/// - <b>Last-known-information for the controller branches (CR 603.10)</b>:
///   both dies-triggers branch on the dying creature's controller read
///   from <see cref="CardMovedEvent.LkiController"/> — the snapshot
///   captured at the instant of death by
///   <see cref="Majik.Core.Services.ZoneService"/>, BEFORE the
///   battlefield-exit controller reset (CR 110.2). A creature an opponent
///   has stolen from you (Act of Treason) that dies correctly counts as
///   "a creature an opponent controls"; one you have stolen counts as "a
///   creature you control".
/// </summary>
[CardName("The Meathook Massacre")]
public static class TheMeathookMassacreFactory
{
    public const string CardName = "The Meathook Massacre";
    public const string PrintedManaCost = "{X}{B}{B}";

    /// <summary>
    /// Construct The Meathook Massacre with no live runtime services.
    /// All three triggered abilities are attached to the card shape;
    /// none are registered with a <see cref="TriggerManager"/>, no
    /// opponent resolver is wired (so the own-creature dies drain is a
    /// no-op), and the ETB sweep reads X = <c>PendingCastX ?? 0</c>
    /// against the controller's battlefield only. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct The Meathook Massacre with optional runtime services.
    /// <paramref name="triggers"/> registers all three triggered abilities so
    /// the bus drives them automatically. The ETB sweep (all players' creatures)
    /// and the own-creature-dies drain (each opponent) both read the live game
    /// off the resolution context at resolution (<see cref="ContextOpponents"/>
    /// / <c>rc.Game.AllPlayers</c>), so they are correct on the production
    /// routed build — the ETB sweep no longer silently scans only the
    /// controller's battlefield, and the drain is no longer inert.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            supertypes: new[] { CardSupertype.Legendary });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB sweep — CR 603.6a / CR 122.1g.
        //   "When The Meathook Massacre enters, all creatures get -X/-X
        //    until end of turn."
        // X is sampled from PendingCastX (stamped by SpellCastFlow at
        // cast time, after ChooseXAsync). The sweep iterates every
        // battlefield via allPlayersResolver?.Invoke() — falls back to
        // controller-only when no resolver is supplied (same convention
        // as Pernicious Deed). For each creature, register a per-creature
        // PumpUntilEndOfTurnEffect on its own ActiveEffects service —
        // matches CountersSpellFactory.AllCreaturesPumpSpell so the
        // -X/-X bonus walks through the standard layer pipeline and
        // expires at end of turn (CR 514.2).
        // ----------------------------------------------------------------
        var etbSweepEffect = new Effect(
            "The Meathook Massacre — all creatures get -X/-X until end of turn",
            rc =>
            {
                var x = card.PendingCastX ?? 0;
                card.ClearPendingCastX();
                if (x <= 0) return ValueTask.CompletedTask;

                // "ALL creatures" — every player's battlefield, read from the
                // LIVE resolution context. Previously a captured
                // allPlayersResolver (null on the routed prod build) made the
                // sweep silently scan ONLY the controller's creatures
                // (resolver-null bug class). Fall back to controller-only when
                // no live game is available (shape path).
                var players = rc.Game?.AllPlayers
                    ?? (IReadOnlyList<Player>)new[] { card.Controller ?? owner };

                foreach (var p in players)
                {
                    foreach (var c in p.Zones.Battlefield.GetCards().OfType<Creature>())
                    {
                        if (c.ActiveEffects != null)
                        {
                            c.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(c, -x, -x));
                        }
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
        //   "Whenever a creature an opponent controls dies, you gain 1
        //    life."
        // Fires on CardMovedEvent Battlefield → Graveyard for any
        // Creature whose controller is NOT the Massacre's controller.
        // CR 603.10 — controller is read from the LKI snapshot
        // (e.LkiController) captured at the instant of death, not the
        // post-reset live card.
        // ----------------------------------------------------------------
        var oppDiesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            // CR 603.10 — controller read from last-known information at the
            // instant of death (e.LkiController), NOT the post-reset live card.
            return !ReferenceEquals(e.LkiController, owner);
        });

        var oppDiesEffect = new Effect(
            "The Meathook Massacre — controller gains 1 life",
            () => owner.GainLife(1));

        var oppDiesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: oppDiesCondition,
            effects: new IEffect[] { oppDiesEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(oppDiesTrigger);
        triggers?.RegisterTriggeredAbility(oppDiesTrigger);

        // ----------------------------------------------------------------
        // Own-creature dies trigger — CR 603.1 + CR 700.4.
        //   "Whenever a creature you control dies, each opponent loses 1
        //    life."
        // Fires on CardMovedEvent Battlefield → Graveyard for any
        // Creature whose controller IS the Massacre's controller. The
        // drain iterates opponentResolver?.Invoke() — without a resolver
        // the drain silently no-ops (mirrors Sheoldred's resolver
        // pattern). A defensive ReferenceEquals filter skips the
        // controller if the resolver returns the full Game.Players list.
        // ----------------------------------------------------------------
        var ownDiesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            // CR 603.10 — controller read from last-known information at the
            // instant of death (e.LkiController), NOT the post-reset live card.
            return ReferenceEquals(e.LkiController, owner);
        });

        var ownDiesEffect = new Effect(
            "The Meathook Massacre — each opponent loses 1 life",
            ctx =>
            {
                // "Each opponent" is read from the LIVE resolution context —
                // NOT a captured resolver, which was null on the routed prod
                // build and made the drain INERT in real games (resolver-null
                // bug class; mirrors Stormbreath #2540 / Grist #2549).
                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    opp.LoseLife(1);
                }
                return ValueTask.CompletedTask;
            });

        var ownDiesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ownDiesCondition,
            effects: new IEffect[] { ownDiesEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ownDiesTrigger);
        triggers?.RegisterTriggeredAbility(ownDiesTrigger);

        return card;
    }
}
