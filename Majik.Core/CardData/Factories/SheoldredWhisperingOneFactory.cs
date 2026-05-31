using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sheoldred, Whispering One (New Phyrexia, {5}{B}{B}).
///
/// Legendary Creature — Praetor 6/6. Oracle text (Scryfall, verified):
///   "Swampwalk
///    At the beginning of your upkeep, return a creature card from your
///    graveyard to the battlefield.
///    At the beginning of each opponent's upkeep, that player sacrifices a
///    creature."
///
/// ## Implemented (v1)
/// - 6/6 Legendary Creature — Praetor, mana cost {5}{B}{B}, owner /
///   controller wired.
/// - <b>Swampwalk</b> as a <see cref="KeywordAbility"/> marker (CR 702.13
///   landwalk variant; <see cref="Majik.Core.Combat.CombatAbilities"/>
///   consumers gate the "can't be blocked" predicate on whether the
///   defending player controls a Swamp). Same wiring as
///   <see cref="StreetWraithFactory"/>.
/// - <b>Your-upkeep trigger (CR 603.1 / 500)</b>: "At the beginning of your
///   upkeep, return a creature card from your graveyard to the battlefield."
///   Modelled as a triggered ability over <see cref="StepStartedEvent"/>
///   filtered to the controller's Upkeep step (<see cref="Triggers.OnStepBegin"/>).
///   On resolution the first creature card in the controller's graveyard is
///   returned to the battlefield under their control (CR 701.20), routing
///   through <see cref="ZoneService.MoveCard"/> when supplied so ETB
///   triggers fire (CR 603.6a). Deterministic first-match pick mirrors
///   <see cref="ReanimateFactory"/> (this clause is NOT a "target" —
///   it is a mandatory "return a creature card", so the controller picks;
///   v1 picks the first creature in graveyard order).
/// - <b>Each-opponent's-upkeep trigger (CR 603.1 / 500)</b>: "At the
///   beginning of each opponent's upkeep, that player sacrifices a creature."
///   Modelled as a triggered ability over <see cref="StepStartedEvent"/>
///   filtered to ANY player that is not the controller (an opponent — CR
///   102.1) entering their Upkeep step. On resolution, the player whose
///   upkeep it is ("that player") sacrifices a creature of their choice. The
///   victim's agent drives the pick (<see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>,
///   intent <see cref="BotIntent.Removal"/>) with a deterministic fallback
///   to the first creature in battlefield order — mirrors
///   <see cref="DiabolicEdictFactory"/>. CR 701.16 — sacrifice moves the
///   permanent to its owner's graveyard, bypassing Indestructible / regen.
///   An opponent controlling no creatures sacrifices nothing (no-op).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Both upkeep triggers
///   are attached for shape inspection (not registered with a
///   <see cref="TriggerManager"/>); no <see cref="ZoneService"/> so the
///   your-upkeep return uses the raw-zone fallback. Suitable for dispatcher
///   / shape / isolated-effect tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?)"/>
///   — fully wired. When <paramref name="triggers"/> is supplied both
///   triggers are registered so <see cref="StepStartedEvent"/> auto-queues
///   them. When <paramref name="zoneService"/> is supplied the reanimation
///   move routes through <see cref="ZoneService.MoveCard"/> so ETB triggers
///   fire (CR 603.6a).
///
/// ## Deferred (v1 gaps)
/// - <b>"Return a creature card" controller prompt</b>: the your-upkeep
///   return picks the first creature card in graveyard order
///   deterministically — same posture as <see cref="ReanimateFactory"/>;
///   surfacing the pick to an agent prompt is deferred.
/// </summary>
[CardName("Sheoldred, Whispering One")]
public static class SheoldredWhisperingOneFactory
{
    public const string CardName = "Sheoldred, Whispering One";
    public const string PrintedManaCost = "{5}{B}{B}";
    public const int Power = 6;
    public const int Toughness = 6;

    /// <summary>
    /// Construct Sheoldred, Whispering One with no live wiring. Both upkeep
    /// triggers are attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>); the your-upkeep return uses the
    /// raw-zone fallback (no <see cref="ZoneService"/>).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Sheoldred, Whispering One. When <paramref name="triggers"/>
    /// is supplied both upkeep triggers are registered so
    /// <see cref="StepStartedEvent"/> auto-queues them. When
    /// <paramref name="zoneService"/> is supplied the your-upkeep reanimation
    /// routes through <see cref="ZoneService.MoveCard"/> so ETB triggers fire
    /// (CR 603.6a).
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Praetor });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Swampwalk — CR 702.13 (landwalk). KeywordAbility marker only;
        // CombatAbilities consumers gate the "can't be blocked" predicate
        // on whether the defending player controls a Swamp. Same posture as
        // StreetWraithFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Swampwalk", card, owner));

        // ----------------------------------------------------------------
        // Your-upkeep trigger — CR 603.1 / 500.
        //   "At the beginning of your upkeep, return a creature card from
        //    your graveyard to the battlefield."
        // Triggers.OnStepBegin filters StepStartedEvent to the controller's
        // own Upkeep step. On resolution, return the first creature card in
        // the controller's graveyard to the battlefield under their control
        // (CR 701.20). NOT a "target" — mandatory "return a creature card";
        // v1 deterministic first-match (mirrors ReanimateFactory).
        // ----------------------------------------------------------------
        var reanimateEffect = new Effect(
            $"{CardName}: return a creature card from your graveyard to the battlefield",
            () =>
            {
                var pick = owner.Zones.Graveyard.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault();
                if (pick == null) return; // empty / no creature card → no-op

                // CR 701.20 — graveyard → battlefield under your control.
                // Routes through ZoneService when supplied so ETB triggers
                // fire (CR 603.6a); raw-zone fallback otherwise.
                Fx.ReturnFromGraveyardToBattlefield(pick, owner, zoneService);
            });

        var yourUpkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.Upkeep),
            effects: new IEffect[] { reanimateEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(yourUpkeepTrigger);
        triggers?.RegisterTriggeredAbility(yourUpkeepTrigger);

        // ----------------------------------------------------------------
        // Each-opponent's-upkeep trigger — CR 603.1 / 500.
        //   "At the beginning of each opponent's upkeep, that player
        //    sacrifices a creature."
        // Fires on StepStartedEvent for the Upkeep step of ANY player that
        // is not the controller (an opponent — CR 102.1). "That player"
        // (the one whose upkeep it is) sacrifices a creature of their choice
        // (CR 701.16). The victim's agent drives the pick with a
        // deterministic first-creature fallback (mirrors DiabolicEdict).
        //
        // We capture the triggering player by reading the event off the
        // condition via a closure-held field updated by the predicate, so
        // the resolution body knows which opponent's upkeep fired it.
        // ----------------------------------------------------------------
        Player? triggeringOpponent = null;

        var opponentUpkeepCondition = new EventTriggerCondition<StepStartedEvent>((e, _) =>
        {
            if (e.StepType != PhaseStateType.Upkeep) return false;
            // CR 102.1 — an opponent is any player other than the controller.
            if (ReferenceEquals(e.Player, owner)) return false;
            triggeringOpponent = e.Player;
            return true;
        });

        var opponentSacrificeEffect = new Effect(
            $"{CardName}: that player sacrifices a creature",
            async ctx =>
            {
                var victim = triggeringOpponent;
                if (victim == null) return; // condition not satisfied → no-op

                // Pre-filter the opponent's battlefield to creatures they
                // control (legal sacrifice picks).
                var candidates = victim.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Cast<ICard>()
                    .ToList();

                // No creature → no-op (CR 701.16 can't be executed when the
                // player controls no creatures; the ability still resolves).
                if (candidates.Count == 0) return;

                // "Sacrifices a creature" — the victim chooses. Their agent
                // drives the pick (BotIntent.Removal) with a deterministic
                // fallback to the first creature in battlefield order.
                ICard sacrificed;
                var agent = ctx.Agent ?? AgentRegistry.Get(victim);
                if (agent != null)
                {
                    var chosen = agent
                        .ChooseFromBattlefieldAsync(victim, candidates, BotIntent.Removal)
                        .GetAwaiter().GetResult();

                    // Validate the agent pick: must still be a creature on
                    // the victim's battlefield. Invalid → deterministic
                    // fallback (mirrors DiabolicEdictFactory).
                    sacrificed = (chosen != null
                                  && chosen.Zone == ZoneType.Battlefield
                                  && ReferenceEquals(chosen.Controller, victim)
                                  && chosen.HasType(CardType.Creature))
                        ? chosen
                        : candidates[0];
                }
                else
                {
                    sacrificed = candidates[0];
                }

                // CR 701.16 — sacrifice: move permanent to its owner's
                // graveyard. Bypasses Indestructible / regeneration.
                Fx.MoveToGraveyard(sacrificed, ZoneMoveReason.Sacrifice);
            });

        var opponentUpkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: opponentUpkeepCondition,
            effects: new IEffect[] { opponentSacrificeEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(opponentUpkeepTrigger);
        triggers?.RegisterTriggeredAbility(opponentUpkeepTrigger);

        return card;
    }
}
