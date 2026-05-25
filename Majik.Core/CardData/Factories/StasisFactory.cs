using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stasis (Limited Edition Alpha, {1}{U}).
///
/// Enchantment. Oracle text:
///   "Players skip their untap steps."
///   "At the beginning of your upkeep, sacrifice Stasis unless you pay {U}."
///
/// ## Implemented (v1)
/// - Enchantment {1}{U} with owner/controller wiring.
/// - <b>"Players skip their untap steps" static (CR 502.1)</b>: wired via
///   <see cref="UntapCountCapStaticEffect"/> with <c>MaxCount = 0</c> and a
///   filter that matches every <see cref="Permanent"/>. While Stasis is on
///   the battlefield, both players' untap-step candidate lists are emptied
///   by the cap pass — functionally equivalent to "skip the entire untap
///   step" (the engine's per-permanent untap loop is gated for every
///   candidate). Symmetric — affects every player, matching the printed
///   oracle's "players" phrasing. On LTB the registration lifts. Pass an
///   <see cref="IEventBus"/> to
///   <see cref="Create(Player, TriggerManager?, IEventBus?)"/> to activate
///   the lifecycle (it sync-attaches via <see cref="CardMovedEvent"/>).
/// - <b>Upkeep pay-or-sacrifice trigger (CR 603.1 / CR 500.4)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="StepStartedEvent"/>
///   filtered to (Upkeep, controller). At resolution the effect attempts
///   <see cref="Player.PayMana"/> with {U} against the controller's mana
///   pool. If the payment succeeds Stasis stays; if it fails the effect
///   sacrifices Stasis (Battlefield → Graveyard). v1 "may" collapses to
///   pay-if-able — same posture as <see cref="ManaVaultFactory"/>'s
///   upkeep trigger and the pact cycle.
///
/// ## Deferred (v1 gaps)
/// - <b>"Skip the untap step" wholesale</b>: the engine has no
///   "skip an entire step" surface; v1 expresses Stasis through the
///   count-cap primitive with MaxCount=0. Functionally equivalent
///   (no permanent untaps under Stasis) and re-uses the same registry
///   wiring Static Orb / Winter Orb / Smoke ride on.
/// - <b>Cost-payment prompt</b>: there's no agent prompt for "do you
///   want to pay {U}?" yet — same gap as Mana Vault / pact cycle. v1
///   auto-pays if the controller's pool has {U}; otherwise the sacrifice
///   tail fires. Real prompt deferred until <see cref="IPlayerAgent"/>
///   grows a ChooseYesNoAsync surface.
/// </summary>
[CardName("Stasis")]
public static class StasisFactory
{
    public const string CardName = "Stasis";
    public const string PrintedManaCost = "{1}{U}";
    public const string UpkeepCost = "{U}";

    /// <summary>
    /// Shape-only constructor — builds Stasis with correct identity, plus
    /// the upkeep trigger attached to its ability list. Neither the static
    /// "players skip their untap steps" lifecycle nor live TriggerManager
    /// registration is wired; pass overloads to activate.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Stasis with optional <see cref="TriggerManager"/> wiring
    /// for the upkeep trigger; the printed static stays shape-only without
    /// an event bus.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers) =>
        Create(owner, triggers, eventBus: null);

    /// <summary>
    /// Construct Stasis with optional trigger-manager + event-bus wiring.
    /// When <paramref name="eventBus"/> is supplied, the
    /// <see cref="UntapCountCapStaticEffect"/> lifecycle attaches so the
    /// printed "Players skip their untap steps" clause activates on ETB
    /// and lifts on LTB (CR 502.1).
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1, CR 500.4. "At the beginning of your
        // upkeep, sacrifice Stasis unless you pay {U}." v1 auto-pays from
        // the controller's mana pool if able; on failure, move Stasis
        // Battlefield → Graveyard (sacrifice).
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            "Stasis: at upkeep, sacrifice unless you pay {U}",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;
                var cost = ManaCost.Parse("U");

                if (!controller.PayMana(cost))
                {
                    // Sacrifice — Battlefield → Graveyard. Raw zone move
                    // (same shape as NihilSpellbombFactory.SacrificeSelf).
                    controller.Zones.Battlefield.RemoveCard(card);
                    controller.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // CR 502.1 — "Players skip their untap steps." Expressed via the
        // count-cap primitive: MaxCount = 0 on a match-everything filter
        // is equivalent to "no permanent untaps". Symmetric (no player
        // filter — printed wording is "players").
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            new UntapCountCapStaticEffect(
                source: card,
                maxCount: 0,
                filter: _ => true,
                isActive: () => true,
                eventBus: eventBus).Attach();
        }

        return card;
    }
}
