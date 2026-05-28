using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glimmervoid (Mirrodin / reprints).
///
/// Land. Oracle text:
///   "At the beginning of the end step, if you control no artifacts,
///    sacrifice this land."
///   "{T}: Add one mana of any color."
///
/// ## Implementation (v1)
///
/// ### "{T}: Add one mana of any color."
/// Modelled as five <see cref="ManaAbility"/> instances (one per WUBRG),
/// same pattern as <see cref="MoxOpalFactory"/> and Delighted Halfling. Each
/// ability is gated on the land being untapped (CR 605.1 — mana abilities do
/// not use the stack). The bot's source-picker selects the appropriate colour
/// at payment time.
///
/// ### "At the beginning of the end step, if you control no artifacts,
///      sacrifice this land." (CR 603.4 — intervening if)
/// Wired as a <see cref="TriggeredAbility"/> over
/// <see cref="StepStartedEvent"/> filtered to
/// <c>StepType == End &amp;&amp; Player == controller</c> (your end step only).
///
/// CR 603.4 — "intervening if" clause: the condition is checked at BOTH the
/// trigger event (via <see cref="TriggeredAbility.IsTriggered"/>'s
/// <see cref="TriggeredAbility.InterveningIf"/> delegate) AND at resolution
/// (first line of the effect body). If the controller acquires an artifact
/// after the trigger fires but before it resolves, the effect is a no-op at
/// resolution (the if-clause is false again).
///
/// The intervening-if predicate: "you control no artifacts" → the
/// controller's battlefield has zero permanent cards with
/// <see cref="CardType.Artifact"/>. Glimmervoid itself is a Land, not an
/// Artifact, so it does not count.
///
/// On resolution (if condition still holds): Glimmervoid moves Battlefield →
/// Graveyard (sacrifice — CR 701.16). The effect reads <c>card.Controller</c>
/// at resolve time so a control-change effect routes correctly.
///
/// ### CR 603.6a — zone check
/// The triggered ability's <c>activeZones</c> is restricted to
/// <see cref="ZoneType.Battlefield"/> so the trigger only listens while
/// Glimmervoid is on the battlefield (standard rule for triggered abilities
/// of permanents — CR 603.6).
///
/// ## Deferred (v1 gaps)
/// - "Add one mana of any color" is five separate ManaAbility instances;
///   a single modal-colour ability (choose at activation) is not yet in the
///   engine — same gap as Mox Opal / Delighted Halfling / City of Brass.
/// - The trigger uses <c>Triggers.OnStepBegin(owner, End)</c> to capture
///   "your end step"; multiplayer "each player's end step" semantics are
///   not relevant for Modern (2-player).
/// </summary>
[CardName("Glimmervoid")]
public static class GlimmervoidFactory
{
    public const string CardName = "Glimmervoid";

    /// <summary>
    /// Construct Glimmervoid with correct identity and mana abilities.
    /// The end-step sacrifice trigger is attached to the card shape but
    /// NOT registered with a TriggerManager — suitable for shape / dispatch
    /// tests. Pass the two-arg overload for live trigger wiring.
    /// </summary>
    public static Land Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Glimmervoid. When <paramref name="triggers"/> is supplied,
    /// the end-step sacrifice trigger is registered so a
    /// <see cref="StepStartedEvent"/> with
    /// <see cref="StepStartedEvent.StepType"/> == <see cref="PhaseStateType.End"/>
    /// automatically queues the ability on the stack (CR 603.2).
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Land(CardName, supertypes: null, subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // ------------------------------------------------------------------
        // {T}: Add one mana of any color.
        // Five ManaAbility instances, one per WUBRG. Each is gated on
        // !IsTapped. CR 605.1 — mana abilities do not use the stack.
        // ------------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            card.AddAbility(new ManaAbility(
                source: card,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !card.IsTapped));
        }

        // ------------------------------------------------------------------
        // "At the beginning of the end step, if you control no artifacts,
        //  sacrifice this land." CR 603.4 — intervening if.
        //
        // Trigger: StepStartedEvent(End) on the controller's own turn.
        // InterveningIf: controller has no artifacts on the battlefield.
        //   Checked at trigger time AND at resolution per CR 603.4.
        // Effect: if the condition is still true, sacrifice Glimmervoid
        //   (Battlefield → Graveyard, CR 701.16).
        // ------------------------------------------------------------------

        // The intervening-if predicate (live read at each check point).
        bool ControllerHasNoArtifacts()
        {
            var controller = card.Controller ?? owner;
            foreach (var permanent in controller.Zones.Battlefield.GetCards())
            {
                if (permanent.HasType(CardType.Artifact))
                    return false;
            }
            return true;
        }

        var sacEffect = new Effect(
            $"{CardName}: sacrifice if you control no artifacts at end step",
            () =>
            {
                // CR 603.4 — re-check condition at resolution.
                if (!ControllerHasNoArtifacts()) return;

                // Zone guard — must still be on the battlefield.
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 701.16 — sacrifice: controller's battlefield → owner's
                // graveyard (raw zone move, same shape as StasisFactory /
                // NihilSpellbombFactory).
                var controller = card.Controller ?? owner;
                var graveyardOwner = card.Owner ?? owner;

                controller.Zones.Battlefield.RemoveCard(card);
                graveyardOwner.Zones.Graveyard.AddCard(card);
                card.SetZone(ZoneType.Graveyard);
            });

        // Trigger fires only on *your* end step (controller's own
        // StepStartedEvent). Restricts active zone to Battlefield.
        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.End),
            effects: new IEffect[] { sacEffect },
            interveningIf: ControllerHasNoArtifacts,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        return card;
    }

    /// <summary>
    /// Returns true when the given player controls no artifact permanents.
    /// Used externally by bot decision logic that needs to evaluate
    /// Glimmervoid's sac risk without holding a card reference.
    /// </summary>
    public static bool ControlsNoArtifacts(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        foreach (var card in player.Zones.Battlefield.GetCards())
        {
            if (card.HasType(CardType.Artifact))
                return false;
        }
        return true;
    }
}
