using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wilderness Reclamation (Ravnica Allegiance,
/// {3}{G}).
///
/// Enchantment. Oracle text:
///   "At the beginning of each end step, untap all lands you control."
///
/// ## Implemented (v1)
///
/// - Enchantment shape, mana cost {3}{G}.
/// - <b>"At the beginning of each end step" trigger (CR 603.1 /
///   CR 500.7)</b> — wired via a raw
///   <see cref="EventTriggerCondition{T}"/> over
///   <see cref="StepStartedEvent"/> filtered to
///   <c>StepType == End</c>. <b>No active-player filter</b> — printed
///   text reads "each end step", meaning the trigger fires on every
///   player's end step (CR 500.7 "each player"). This is intentionally
///   different from <see cref="Abilities.Triggers.OnStepBegin"/>'s
///   controller-only gating; Wilderness Reclamation is the classic
///   abuse case for the difference (untaps lands for both your end step
///   and your opponent's end step — though only your own end-step untap
///   actually matters since you only need mana on your own turns; the
///   opponent's end-step untap is wasted but does fire by the printed
///   rules).
/// - On resolution: untap every <see cref="Land"/> the enchantment's
///   controller controls on the battlefield (CR 701.20 — untap a
///   permanent). Each <see cref="Permanent.Untap"/> call is guarded by
///   an <see cref="Permanent.IsTapped"/> check because
///   <see cref="Permanent.Untap"/> throws on an already-untapped
///   permanent (same posture as
///   <see cref="SwordOfFeastAndFamineFactory"/>'s combat trigger).
/// - The trigger reads <c>card.Controller</c> at resolve time so a
///   control-change effect (Confiscate, Threaten) routes the untap to
///   the new controller correctly.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits trigger-
/// manager wiring and produces the correct card shape only — suitable
/// for factory-shape / dispatch tests. The end-step trigger is attached
/// to the card for shape observability but is not registered with any
/// <see cref="TriggerManager"/>; tests fire it manually via
/// <see cref="TriggeredAbility.IsTriggered"/> or by executing the effect
/// directly. The two-arg overload registers it for bus-driven firing.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Multiplayer "each player"</b>: in two-player Modern, "each end
///   step" = (active player's end step) + (opponent's end step) per
///   turn. v1's predicate matches both correctly because there's no
///   active-player filter. When multiplayer ships, the same predicate
///   fires once per player's end step with no change — the printed
///   "each" already aligns with the multi-player semantics.
/// </summary>
[CardName("Wilderness Reclamation")]
public static class WildernessReclamationFactory
{
    public const string CardName = "Wilderness Reclamation";
    public const string PrintedManaCost = "{3}{G}";

    /// <summary>
    /// Construct Wilderness Reclamation with no live trigger-manager
    /// wiring (shape / dispatcher path). The end-step trigger is
    /// attached to the card so shape / dispatcher tests see the ability
    /// shape, but it is not registered with any
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Wilderness Reclamation. When <paramref name="triggers"/>
    /// is supplied the end-step trigger is registered so a
    /// <see cref="StepStartedEvent"/> with
    /// <see cref="StepStartedEvent.StepType"/> ==
    /// <see cref="PhaseStateType.End"/> automatically queues the ability
    /// on the stack (CR 603.2).
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "At the beginning of each end step, untap all lands you
        // control." CR 603.1 / CR 500.7. No active-player filter — the
        // printed "each" fires on every player's end step.
        // ----------------------------------------------------------------
        var untapEffect = new Effect(
            $"{CardName}: untap all lands the controller controls",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 110.2 — read controller at resolve time so a control
                // change (Confiscate / Threaten) routes the untap to the
                // current controller, not the original caster.
                var controller = card.Controller ?? owner;

                // CR 701.20 — untap each Land the controller controls.
                // Permanent.Untap() throws on an already-untapped
                // permanent, so each call is gated by IsTapped.
                foreach (var land in controller.Zones.Battlefield.GetCards().OfType<Land>())
                {
                    if (land.IsTapped) land.Untap();
                }
            });

        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End),
            effects: new IEffect[] { untapEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        return card;
    }
}
