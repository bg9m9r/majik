using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prized Amalgam (Shadows over Innistrad, {3}{U/B}).
///
/// Creature — Zombie Horror 3/3. Oracle text:
///   "Whenever another creature you control enters under your control,
///    if Prized Amalgam is in your graveyard, return Prized Amalgam to
///    the battlefield tapped at the beginning of the next end step."
///
/// ## Implemented (v1)
/// - 3/3 Zombie Horror. Printed mana cost <c>{3}{U/B}</c> rendered as a
///   plain <c>{3}{U/B}</c> string. The factory does NOT wire a hybrid-
///   mana payment alternative — same simplification as
///   <see cref="VaultSkirgeFactory"/>'s Phyrexian rendering — the printed
///   string round-trips through dispatch / shape tests.
/// - <b>Graveyard-resident trigger (CR 603.6d)</b>: a
///   <see cref="TriggeredAbility"/> watches <see cref="CardMovedEvent"/>
///   filtered to <c>ToZone == Battlefield</c>, Creature, !self, controller
///   matches Amalgam's owner. <c>activeZones = {Graveyard}</c> so the
///   ability only fires while Amalgam is in its owner's graveyard.
/// - <b>Intervening-if (CR 603.4)</b>: the printed "if Prized Amalgam is
///   in your graveyard" is encoded as both the activeZones filter (the
///   ability can't fire at all otherwise) and a defensive
///   <c>interveningIf</c> re-check at trigger-evaluation time.
/// - <b>Delayed trigger (CR 603.7 / CR 603.7c)</b>: on resolve, the
///   trigger registers a <see cref="DelayedTriggeredAbility"/> on the
///   supplied <see cref="TriggerManager"/> that fires on the NEXT
///   <see cref="StepStartedEvent"/> with
///   <see cref="PhaseStateType.End"/>. The delayed effect re-checks
///   Amalgam's zone at resolve time and returns it from graveyard to
///   battlefield TAPPED via <see cref="ZoneService.MoveCard"/> (so ETB
///   triggers fire — CR 603.6a) with a post-move <see cref="Permanent.Tap"/>
///   so tapped-entry is observable for downstream watchers.
///   "Next end step" is enforced by a one-shot guard: the delayed
///   trigger captures a registration timestamp and only fires on
///   <see cref="StepStartedEvent.Timestamp"/> &gt; that mark, matching
///   the <see cref="GoryosVengeanceFactory"/> exile-at-EOT pattern.
///
/// ## Deferred (v1 gaps)
/// - <b>Hybrid mana cost</b>: <c>{3}{U/B}</c> is stored as a plain
///   printed string. The cast-flow doesn't yet branch on hybrid colour
///   payment — same gap as every other hybrid-cost card in Modern
///   (Boros Reckoner, Murderous Redcap, Manamorphose).
/// - <b>"At the beginning of the NEXT end step"</b>: the delayed trigger
///   fires on the first End step after registration. If multiple
///   creatures ETB on the same turn while Amalgam sits in the graveyard,
///   each registers its own delayed trigger — they all fire on the same
///   end step but Amalgam only returns once (the zone re-check at
///   resolve time short-circuits subsequent triggers). This matches the
///   printed rules text.
/// - <b>Token ETB</b>: printed text doesn't qualify "nontoken"; tokens
///   entering also trigger Amalgam. Faithful to oracle.
/// </summary>
[CardName("Prized Amalgam")]
public static class PrizedAmalgamFactory
{
    public const string CardName = "Prized Amalgam";
    public const string PrintedManaCost = "{3}{U/B}";

    /// <summary>
    /// Construct Prized Amalgam with no runtime service wiring. Shape /
    /// dispatch path — the graveyard-resident trigger is attached
    /// structurally but not registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Prized Amalgam with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone-service used by the delayed end-step
    /// trigger to move Amalgam from graveyard to battlefield so ETB triggers
    /// fire (CR 603.6a). May be null — raw zone move performed instead.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident
    /// + delayed trigger registration (CR 603.6d / CR 603.7). May be null —
    /// trigger is attached structurally but not registered.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 3,
            toughness: 3,
            subtypes: new[] { CardSubtype.Zombie, CardSubtype.Horror });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Graveyard-resident trigger — CR 603.1 + CR 603.6d.
        //   "Whenever another creature you control enters under your
        //    control, if Prized Amalgam is in your graveyard, return
        //    Prized Amalgam to the battlefield tapped at the beginning
        //    of the next end step."
        //
        // Two-stage shape:
        //   Stage 1 — ETB trigger fires (activeZones = {Graveyard}),
        //             register a delayed trigger for next end step.
        //   Stage 2 — delayed trigger fires at end step, return Amalgam
        //             to the battlefield tapped.
        // ----------------------------------------------------------------
        var anotherCreatureEtbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (ReferenceEquals(e.Card, card)) return false; // "another"
            // "under your control" — the entering card's controller is
            // assessed on the live battlefield state after the move
            // completes (CR 614.6).
            return ReferenceEquals(e.Card.Controller, owner);
        });

        var registerDelayedEffect = new Effect(
            $"{CardName}: register delayed end-step return (tapped)",
            () =>
            {
                if (triggers == null) return;
                // CR 603.7 — capture the registration moment so the
                // delayed trigger fires on the NEXT End step, not one
                // that has already fired in the same priority window.
                var registeredAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();

                var returnTappedEffect = new Effect(
                    $"{CardName}: return from graveyard to battlefield tapped (delayed)",
                    () =>
                    {
                        // CR 608.2b — re-check at resolve time. If
                        // Amalgam has left the graveyard between
                        // registration and end step, no-op (matches the
                        // "still in graveyard at end step" implicit
                        // requirement — and short-circuits the duplicate-
                        // registration case where multiple ETB triggers
                        // queued multiple delayed triggers).
                        if (card.Zone != ZoneType.Graveyard) return;
                        if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                        if (zoneService != null)
                        {
                            zoneService.MoveCard(
                                card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
                        }
                        else
                        {
                            owner.Zones.Graveyard.RemoveCard(card);
                            owner.Zones.Battlefield.AddCard(card);
                            card.SetZone(ZoneType.Battlefield);
                            card.SetController(owner);
                            // Manual MarkEnteredBattlefield equivalent for
                            // the raw-zone path is internal; we skip it
                            // (raw-path is shape-only).
                        }

                        // Tapped-entry — apply AFTER the move so the
                        // permanent is on the battlefield (CR 614 / Tap
                        // requires battlefield zone).
                        if (card.Zone == ZoneType.Battlefield && !card.IsTapped)
                        {
                            card.Tap();
                        }
                    });

                var delayed = new DelayedTriggeredAbility(
                    source: card,
                    controller: owner,
                    condition: new EventTriggerCondition<StepStartedEvent>(
                        (e, _) => e.StepType == PhaseStateType.End
                                  && e.Timestamp > registeredAt),
                    effects: new IEffect[] { returnTappedEffect });

                triggers.RegisterDelayed(delayed);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: anotherCreatureEtbCondition,
            effects: new IEffect[] { registerDelayedEffect },
            // CR 603.4 — intervening-if: "if Prized Amalgam is in your
            // graveyard". ActiveZones already enforces this; the explicit
            // interveningIf is a belt-and-braces re-check at trigger
            // evaluation time (matches Bridge from Below's posture).
            interveningIf: () => card.Zone == ZoneType.Graveyard
                                 && owner.Zones.Graveyard.GetCards().Contains(card),
            // CR 603.6d — ActiveZones = {Graveyard}.
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
