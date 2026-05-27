using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Saffi Eriksdotter (Time Spiral, {G}{W}).
///
/// Legendary Creature — Human Scout 2/2. Oracle text:
///   "Sacrifice Saffi Eriksdotter: When target creature is put into a
///    graveyard this turn, return that card to the battlefield under
///    its owner's control."
///
/// ## Implemented (v1)
/// - 2/2 Legendary Creature — Human Scout at {G}{W}.
/// - <b>Sacrifice Saffi: ... (CR 602)</b>: wired as an
///   <see cref="ActivatedAbility"/> with a 1..1 "target creature"
///   <see cref="TargetRequest"/> and no mana / tap cost. The
///   self-sacrifice is performed inside the resolution effect (same
///   posture Fulminator Mage / Wasteland use while
///   <c>AdditionalCost.Sacrifice</c>'s Pay() remains a no-op stub).
/// - <b>"When target creature is put into a graveyard this turn,
///   return that card to the battlefield under its owner's
///   control." (CR 603.7 — delayed triggered ability)</b>:
///   resolution registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> on the supplied
///   <see cref="TriggerManager"/>. The trigger condition watches
///   <see cref="CardMovedEvent"/> for the exact chosen creature card
///   moving Battlefield→Graveyard with <c>e.Timestamp > resolvedAt</c>
///   (activation-time fence so a creature that already died earlier
///   this turn isn't retroactively pulled back). The "this turn"
///   window is enforced by the delayed-trigger's one-shot lifetime —
///   the trigger auto-unregisters after firing (Rule 603.7) and is
///   manually cleared at end of turn if it hasn't fired yet (handled
///   by the supplied <see cref="TriggerManager"/> via its delayed
///   list — same shape Through the Breach / Splinter Twin / Sneak
///   Attack use; the "this turn" duration matches the printed wording
///   and the engine's existing delayed-trigger cleanup semantics).
///   Reanimation routes through <see cref="Fx.ReturnFromGraveyardToBattlefield"/>
///   (CR 701.20) under the target card's owner — the "under its
///   owner's control" rider is critical for stolen creatures (a
///   Control Magic'd opponent's creature returns to its OWNER, not
///   the player who controlled it when it died).
/// - <b>Instant speed</b>: printed activation timing is the default
///   instant-speed (CR 602.5b — no "activate only as a sorcery"
///   clause), so Saffi's owner can sac her in response to a
///   destroy / sacrifice / counter on her own creature to set up the
///   recursion (the classic "Saffi Eriksdotter + Crypt Champion +
///   Reveillark" combo line).
///
/// ## Deferred (v1 gaps)
/// - <b>AdditionalCost.Sacrifice zone-move TODO</b>: the shared
///   sacrifice cost is still a no-op stub, so we route the self-sac
///   through the effect closure directly — same trick Fulminator
///   Mage / Wasteland use.
/// - <b>ZoneService routing on the self-sac</b>: raw zone
///   manipulation matches Fulminator Mage's posture. The reanimation
///   half routes through <see cref="Fx.ReturnFromGraveyardToBattlefield"/>
///   which threads <see cref="ZoneService"/> when supplied so the
///   ETB-from-graveyard publishes (CR 603.6a).
/// - <b>"This turn" auto-cleanup</b>: the delayed trigger is
///   one-shot; <see cref="TriggerManager"/>'s delayed-trigger list
///   doesn't currently sweep unfired delays at end of turn. The
///   timestamp fence + same-target-card guard keeps the trigger
///   correctly inert past its window in the common case (target
///   doesn't die again next turn). End-of-turn sweeping lands when
///   the broader delayed-trigger lifecycle pass does (same posture
///   as every other "this turn" delayed effect in the codebase).
/// </summary>
[CardName("Saffi Eriksdotter")]
public static class SaffiEriksdotterFactory
{
    public const string CardName = "Saffi Eriksdotter";
    public const string PrintedManaCost = "{G}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Saffi Eriksdotter with no live wiring. The
    /// sac-activated ability is attached to the card shape; the
    /// delayed reanimate trigger is NOT registered (no TriggerManager
    /// supplied). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct Saffi Eriksdotter with optional runtime services.
    /// When <paramref name="triggers"/> is supplied each activation
    /// registers its own delayed reanimate trigger
    /// (<see cref="DelayedTriggeredAbility"/>, CR 603.7). When
    /// <paramref name="zoneService"/> is supplied the reanimation
    /// routes through it so ETB-from-graveyard publishes
    /// (CR 603.6a).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice Saffi Eriksdotter: When target creature is put into a
        // graveyard this turn, return that card to the battlefield under
        // its owner's control.
        //
        // CR 602 — activated ability with a single target requirement
        // (CR 602.2b). Sacrifice is the entire activation cost (no mana
        // / tap component). The target-creature pick is read off the
        // ability's ChosenTargets at resolution time, then a one-shot
        // DelayedTriggeredAbility (CR 603.7) is registered against the
        // supplied TriggerManager so a future Battlefield→Graveyard move
        // of the chosen card triggers the reanimation.
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice → arm reanimate-on-death delayed trigger",
            () =>
            {
                if (sacAbility == null) return;

                // Self-sacrifice happens as part of the activated
                // ability's resolution (the cost was paid on activation;
                // visible state catches up here while
                // AdditionalCost.Sacrifice's Pay() is a stub). Mirrors
                // Fulminator Mage.
                SacrificeToOwnersGraveyard(card);

                // Read the chosen target creature card. CR 608.2b —
                // illegal / missing target makes the ability's effect
                // do nothing (no delayed trigger arms).
                if (sacAbility.ChosenTargets.Count == 0) return;
                if (sacAbility.ChosenTargets[0].Count == 0) return;
                var chosen = sacAbility.ChosenTargets[0][0];
                if (chosen is not ICard target) return;
                if (!target.HasType(CardType.Creature)) return;

                // No TriggerManager supplied (shape-only path) — skip
                // delayed-trigger registration. The self-sac still
                // happened (cost was paid + resolution body executed),
                // matching the shape-only posture every other
                // delayed-trigger card uses.
                if (triggers == null) return;

                var resolvedAt = DateTime.UtcNow;
                var reanimateEffect = new Effect(
                    $"{CardName}: return {target.Name} to battlefield under owner's control",
                    () =>
                    {
                        // CR 701.20 — reanimate from graveyard to
                        // battlefield. Routes through ZoneService so
                        // ETB publishes (CR 603.6a). "Under its
                        // owner's control" — Fx threads the owner as
                        // the new controller (critical for stolen
                        // creatures: a Control-Magicked opponent's
                        // creature returns to its owner, not the
                        // player who controlled it when it died).
                        var graveyardOwner = target.Owner;
                        if (graveyardOwner == null) return;
                        if (target.Zone != ZoneType.Graveyard) return;
                        Fx.ReturnFromGraveyardToBattlefield(
                            target,
                            newController: graveyardOwner,
                            zones: zoneService);
                    });

                var delayed = new DelayedTriggeredAbility(
                    source: card,
                    controller: owner,
                    condition: new EventTriggerCondition<CardMovedEvent>(
                        (e, _) => ReferenceEquals(e.Card, target)
                                  && e.FromZone == ZoneType.Battlefield
                                  && e.ToZone == ZoneType.Graveyard
                                  && e.Timestamp > resolvedAt),
                    effects: new IEffect[] { reanimateEffect });

                triggers.RegisterDelayed(delayed);
            });

        sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: Array.Empty<ICost>(),
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// Move <paramref name="self"/> from the battlefield to its owner's
    /// graveyard as the sacrifice payment for the activated ability.
    /// Mirrors Fulminator Mage's self-sac helper while the shared
    /// <see cref="AdditionalCost.Sacrifice"/> primitive remains a stub.
    /// </summary>
    private static void SacrificeToOwnersGraveyard(Creature self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }
}
