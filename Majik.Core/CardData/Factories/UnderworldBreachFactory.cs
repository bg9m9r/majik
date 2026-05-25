using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Underworld Breach (Theros Beyond Death, {1}{R}).
///
/// Enchantment. Oracle text:
///   "Each nonland card in your graveyard has escape and 'Escape—[card's
///    printed mana cost], Exile three other cards from your graveyard.'
///    (You may cast cards with escape from your graveyard.)
///    At the beginning of the end step, sacrifice Underworld Breach."
///
/// ## Implemented (v1)
/// - Enchantment {1}{R}, owner / controller wired.
/// - <b>Static "grants escape" effect (CR 702.143)</b> — on ETB, stamps a
///   <see cref="Card.GrantRuntimeEscape"/> on every nonland card currently
///   in the controller's graveyard, using each card's own printed mana
///   cost as the escape mana cost and a count of 3 for the
///   "exile N other cards from your graveyard" rider. The
///   <see cref="Majik.Core.Players.Agents.EscapeAltCostProbe.DefaultLookup"/>
///   consults <see cref="Card.RuntimeEscapeCost"/> so the granted escape
///   surfaces to the bot's alt-cost enumeration alongside the printed-
///   escape ship list. The cast itself routes through the normal Escape
///   pipeline (<see cref="Majik.Core.Costs.EscapeAlternativeCost"/> +
///   <see cref="Majik.Core.Game.SpellCastFlow"/>) — Underworld Breach
///   reuses the primitive, it does not duplicate it.
/// - <b>LTB cleanup (CardMovedEvent driven, mirroring Yawgmoth's Will
///   posture)</b> — when an <see cref="IEventBus"/> is supplied, the
///   ETB grant subscribes a handler that clears the runtime escape stamps
///   on the controller's graveyard cards when Underworld Breach itself
///   leaves the battlefield (LTB) or when its end-step sacrifice trigger
///   resolves. Bus-less shape-only callers manage clear themselves.
/// - <b>End-step sacrifice trigger (CR 500.4 / CR 603.1 / CR 701.16)</b>
///   "At the beginning of the end step, sacrifice Underworld Breach."
///   Registered with the supplied <see cref="TriggerManager"/> so the
///   trigger surfaces on the stack at the start of the controller's
///   End step; resolution moves the source to its owner's graveyard.
///
/// ## Deferred (v1 gaps)
/// - <b>Cards entering the graveyard AFTER Underworld Breach resolves</b>:
///   the static grant is a snapshot at ETB time (same conservative posture
///   as Dress Down's snapshot pool). A real continuous-effect would
///   re-stamp newly arriving graveyard cards on every state change. For
///   the storm-combo use case (mill yourself, then escape) the snapshot
///   is correct since all the cast targets are already in graveyard at
///   ETB time.
/// - <b>"Nonland" filter is checked at ETB time</b>: any nonland card
///   already in graveyard is stamped. A land that later becomes a
///   non-land via some weird effect wouldn't be picked up.
/// - <b>Sorcery-speed restriction on grave-cast instants</b>: Escape is
///   subject to the spell's normal timing restriction (CR 702.143
///   inherits CR 117 — sorceries can only be cast sorcery-speed). The
///   shared <see cref="Majik.Core.Costs.EscapeAlternativeCost"/> defers
///   timing-restriction checks to the engine's normal cast-speed
///   machinery; nothing extra is required here.
/// - <b>Static-ability lifecycle</b>: the v1 lifecycle uses a single ETB
///   stamp + LTB clear via a <see cref="CardMovedEvent"/> handler. A
///   real <see cref="StaticAbility"/> with a continuous-effect layer
///   binder would re-evaluate every state change; deferred.
/// </summary>
[CardName("Underworld Breach")]
public static class UnderworldBreachFactory
{
    public const string CardName = "Underworld Breach";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>CR 702.143 — the rider exiles three other graveyard cards.</summary>
    public const int EscapeExileCount = 3;

    /// <summary>
    /// Construct Underworld Breach with no live event-bus or trigger-
    /// manager wiring. Suitable for shape / dispatcher / identity tests.
    /// The end-step sacrifice trigger is attached to the card so structural
    /// assertions still see it; the runtime grant routine is exposed via
    /// <see cref="ApplyGraveyardGrants"/> for direct test use.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Underworld Breach with optional runtime wiring. When
    /// <paramref name="eventBus"/> is supplied, the ETB stamping +
    /// LTB cleanup ride on <see cref="CardMovedEvent"/> so the
    /// runtime escape grants attach as Breach enters and clear when it
    /// leaves. When <paramref name="triggers"/> is supplied the end-step
    /// sacrifice trigger is registered for bus-driven firing.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB stamp: grant runtime escape to every nonland card currently
        // in the controller's graveyard (CR 702.143). The probe consults
        // Card.RuntimeEscapeCost so this surfaces in the bot's enumeration;
        // SpellCastFlow's existing escape path handles the actual cast.
        //
        // LTB clear: subscribe to CardMovedEvent so a Breach Battlefield →
        // (anywhere) move clears the stamps on every card the controller
        // owns in their graveyard. Cleaner than relying on the EOT sweep
        // alone — Breach can also be destroyed / bounced before its end-
        // step trigger fires (Splash response). Self-unsubscribes on first
        // matching fire to avoid lingering handlers.
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            // CR 603.6a — ETB grant. Routed via a CardMovedEvent handler so
            // the stamp happens when Breach actually hits the battlefield
            // (matches the Dress Down / Yawgmoth's Will posture: side-effect
            // on the ZoneService publish, not via the Spell.Effects pipeline).
            Action<CardMovedEvent>? etbHandler = null;
            etbHandler = (e) =>
            {
                if (!ReferenceEquals(e.Card, card)) return;
                if (e.ToZone != ZoneType.Battlefield) return;
                ApplyGraveyardGrants(owner);
                // Self-unsubscribe — the grant is a one-shot ETB stamp.
                // Re-entering the battlefield is a new card instance per
                // CR 400.7, so we don't need to keep listening for ETBs.
                if (etbHandler != null) eventBus.Unsubscribe(etbHandler);
            };
            eventBus.Subscribe(etbHandler);

            Action<CardMovedEvent>? ltbHandler = null;
            ltbHandler = (e) =>
            {
                if (!ReferenceEquals(e.Card, card)) return;
                if (e.FromZone != ZoneType.Battlefield) return;
                ClearGraveyardGrants(owner);
                if (ltbHandler != null) eventBus.Unsubscribe(ltbHandler);
            };
            eventBus.Subscribe(ltbHandler);
        }

        // ----------------------------------------------------------------
        // CR 500.4 / CR 603.1 — "At the beginning of the end step,
        // sacrifice Underworld Breach." Triggers.OnStepBegin filters
        // StepStartedEvent on (End, controller). Resolution = move the
        // source to its owner's graveyard (CR 701.16 sacrifice).
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice at the beginning of the end step",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                // CR 701.16 — sacrifice. Bypasses Indestructible /
                // regeneration per CR 702.12b.
                OracleSpellBinder.MoveToGraveyard(card, Majik.Core.Zones.ZoneMoveReason.Sacrifice);
            });

        var endStepSac = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.End),
            effects: new IEffect[] { sacEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepSac);
        triggers?.RegisterTriggeredAbility(endStepSac);

        return card;
    }

    /// <summary>
    /// Stamp <see cref="Card.GrantRuntimeEscape"/> on every nonland card
    /// currently in <paramref name="controller"/>'s graveyard. The granted
    /// cost is each card's own printed mana cost; the rider count is
    /// <see cref="EscapeExileCount"/> (3). Exposed for shape-only callers
    /// that didn't supply an <see cref="IEventBus"/> and want to apply the
    /// grant manually.
    /// </summary>
    public static void ApplyGraveyardGrants(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (var c in controller.Zones.Graveyard.GetCards().ToList())
        {
            if (c is not Card concrete) continue;
            // CR 702.143 only grants escape to nonland cards.
            if (concrete.HasType(CardType.Land)) continue;
            concrete.GrantRuntimeEscape(concrete.ManaCostValue, EscapeExileCount);
        }
    }

    /// <summary>
    /// Clear <see cref="Card.RuntimeEscapeCost"/> on every card in
    /// <paramref name="controller"/>'s graveyard. Called when Breach
    /// leaves the battlefield (LTB) so the escape grants don't persist
    /// past the granter's lifetime. Idempotent — clearing a card that
    /// has no runtime escape stamp is a no-op.
    /// </summary>
    public static void ClearGraveyardGrants(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (var c in controller.Zones.Graveyard.GetCards().ToList())
        {
            if (c is Card concrete)
            {
                concrete.ClearRuntimeEscape();
            }
        }
    }
}
