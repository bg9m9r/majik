using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eternal Witness (Fifth Dawn + many reprints, {1}{G}{G}).
///
/// Creature — Human Shaman 2/1. Oracle text:
///   "When Eternal Witness enters, you may return target card from your
///    graveyard to your hand."
///
/// ## Implemented (v1)
/// - 2/1 Human Shaman, mana cost {1}{G}{G}.
/// - Single ETB <see cref="TriggeredAbility"/> (CR 603.6a) wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a bespoke 1..1
///   <see cref="TargetRequest"/> exposing every card in the controller's
///   graveyard as a candidate. Distinct from
///   <see cref="AnimateDeadFactory"/>'s "creature card" target — Eternal
///   Witness returns ANY card type. Card-target plumbing mirrors
///   Animate Dead's bespoke graveyard-target shape.
/// - Resolution body reads <see cref="TriggeredAbility.ChosenTargets"/>;
///   validates the chosen card is still in the controller's graveyard
///   (CR 608.2b — clean no-op on fizzle); moves Graveyard → Hand via
///   <see cref="ZoneService.MoveCard"/> when supplied (so
///   <see cref="CardMovedEvent"/> fires for any zone-change triggers per
///   CR 603.6a / CR 701.20), otherwise direct-zone mutation.
/// - "You may" is auto-accepted at v1 — same posture as Tireless Tracker /
///   Phlage / Snapcaster Mage's ETB grant + Through the Breach's hand pick.
/// - Empty-graveyard / no-target path is a clean no-op (CR 608.2b).
/// - Single-arg dispatcher path attaches the trigger shape WITHOUT
///   bus-driven wiring (suitable for shape tests). The
///   (owner, zoneService, eventBus, triggers) overload registers the
///   ETB with the supplied <see cref="TriggerManager"/> for bus-driven
///   firing.
/// - Card-target picker fallback: when no agent is registered against the
///   controller and <see cref="TriggeredAbility.ChosenTargets"/> is empty
///   at resolution time, the resolve body picks the first card in the
///   controller's graveyard deterministically (same posture as
///   <see cref="WishclawTalismanFactory"/> / <see cref="TasigurTheGoldenFangFactory"/>'s
///   first-candidate fallback).
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt
///   before triggers resolve — same pattern as Snapcaster Mage. The
///   factory's first-card fallback is the dispatcher-path safety net.
/// - <b>"You may" decline</b>: not modelled — the ability always returns
///   a card if one is available. Same gap as Tireless Tracker / Phlage /
///   Snapcaster Mage.
/// </summary>
public static class EternalWitnessFactory
{
    public const string CardName = "Eternal Witness";
    public const string PrintedManaCost = "{1}{G}{G}";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "When Eternal Witness enters, you may return target card from " +
        "your graveyard to your hand.";

    /// <summary>
    /// Construct Eternal Witness with no runtime wiring. Produces the
    /// correct card identity + ETB trigger shape for dispatcher / shape
    /// tests; the trigger is NOT registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Eternal Witness with optional runtime wiring. When
    /// <paramref name="triggers"/> is supplied the ETB ability is
    /// registered for bus-driven firing; when <paramref name="zoneService"/>
    /// is supplied the Graveyard → Hand move routes through
    /// <see cref="ZoneService.MoveCard"/> so any downstream zone-change
    /// triggers fire (CR 603.6a / CR 701.20).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Eternal Witness enters, you may return target card from
        //    your graveyard to your hand."
        //
        // Bespoke 1..1 TargetRequest mirrors Animate Dead's graveyard-card
        // shape, but with ANY card type allowed (not just creatures). The
        // candidate list is the controller's graveyard at the time the
        // trigger is created — production callers refresh
        // LegalCandidates / ChosenTargets at resolve time via the agent
        // prompt (same posture as Snapcaster Mage).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: return target card from your graveyard to your hand",
            () => ResolveReturnToHand(card, owner, etb, zoneService));

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Cast<object>().ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Shared resolution helper for the ETB return. Reads the trigger's
    /// <see cref="TriggeredAbility.ChosenTargets"/>; falls back to the
    /// first card in the controller's graveyard when no target was set
    /// (deterministic single-arg dispatcher posture — mirrors
    /// <see cref="WishclawTalismanFactory"/>'s tutor fallback +
    /// <see cref="TasigurTheGoldenFangFactory"/>'s agent-null fallback).
    /// Validates the chosen card is STILL in the controller's graveyard
    /// at resolution (CR 608.2b — illegal target → clean no-op). Moves
    /// the card Graveyard → Hand via <see cref="ZoneService.MoveCard"/>
    /// when supplied; otherwise direct-zone mutation.
    /// </summary>
    private static void ResolveReturnToHand(
        Creature witness,
        Player owner,
        TriggeredAbility? etb,
        ZoneService? zoneService)
    {
        // CR 110.2 — "your graveyard" is the controller's graveyard;
        // Witness's controller is the source of truth at resolve time
        // (handles control-change edge cases).
        var controller = witness.Controller ?? owner;

        ICard? picked = null;

        // 1) Honour the agent-set target if present (production path).
        if (etb != null && etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first card in controller's
        // graveyard (single-arg dispatcher path / no-agent posture).
        picked ??= controller.Zones.Graveyard.GetCards().FirstOrDefault();

        // Empty graveyard → clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b illegal-on-resolution check — target must still be
        // in the controller's graveyard. (Cards leaving the graveyard
        // between trigger creation and resolution fizzle the return.)
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!controller.Zones.Graveyard.GetCards().Contains(picked)) return;

        // Move Graveyard → Hand. ZoneService path publishes a
        // CardMovedEvent so any "leaves graveyard" triggers fire
        // (CR 603.6a / CR 701.20).
        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Hand, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(picked);
            controller.Zones.Hand.AddCard(picked);
            picked.SetZone(ZoneType.Hand);
        }
    }
}
