using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Exquisite Blood (Avacyn Restored, {4}{B}{B}).
///
/// Enchantment. Oracle text:
///   "Whenever an opponent loses life, you gain that much life."
///
/// ## Implemented (v1)
/// - Card identity (Enchantment {4}{B}{B}, owner / controller wiring).
/// - <b>Opponent-life-loss → controller-gain triggered ability
///   (CR 119.3 / 603.6a)</b>: Fires on <see cref="LifeChangedEvent"/> where
///   (a) the event's <see cref="LifeChangedEvent.Player"/> is NOT the
///   controller, and (b) the life total strictly decreased
///   (NewLife &lt; PreviousLife). The lost amount is captured at trigger
///   time (CR 603.2a) via a closure-mutable holder. On resolution the
///   controller gains N life (N = PreviousLife − NewLife).
/// - No targets — the gain is auto-applied to the controller.
///
/// ## Combo interaction (Sanguine Bond)
/// Exquisite Blood + Sanguine Bond is the canonical infinite drain combo.
/// See <see cref="SanguineBondFactory"/> for full flow. Loop terminates via
/// SBAs once the opponent's life total hits 0 (CR 704.5a — player with 0
/// life loses; subsequent <see cref="Player.LoseLife"/> calls throw on a
/// lost player, which unwinds the trigger chain).
///
/// Note: the "you gain N life" effect does NOT prompt — the controller is
/// the implicit beneficiary (no target). Lifegain pipeline still routes
/// through any registered <see cref="LifeGainIntent"/> replacements (Boon
/// Reflection, Beacon of Immortality), preserving stacking semantics.
///
/// ## Deferred (v1 gaps)
/// - <b>Multiple opponents</b>: 2HG / multiplayer formats — the printed
///   trigger reads "an opponent" which fires per-opponent. The condition
///   correctly matches any non-controller player; the gain is applied once
///   per matching event. For 1v1 (the engine's Modern target) this is
///   exact.
/// </summary>
[CardName("Exquisite Blood")]
public static class ExquisiteBloodFactory
{
    public const string CardName = "Exquisite Blood";
    public const string PrintedManaCost = "{4}{B}{B}";

    /// <summary>
    /// Construct Exquisite Blood. The life-loss trigger is attached to the
    /// card shape but not registered with a <see cref="TriggerManager"/>.
    /// Suitable for card-shape / dispatcher tests — tests fire the
    /// triggered ability by invoking its effect directly.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Exquisite Blood with the life-loss trigger registered
    /// against <paramref name="triggers"/> when supplied.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Opponent-loss → controller-gain triggered ability —
        // CR 119.3 / 603.6a.
        //   "Whenever an opponent loses life, you gain that much life."
        // Captures the loss delta at trigger time (CR 603.2a) via a
        // mutable holder. The resolved effect reads it and applies the
        // gain to the controller. Same race-window caveat as
        // SanguineBondFactory (per-instance closure, last-write-wins on
        // batched events — acceptable for the 1v1 combo flow).
        // ----------------------------------------------------------------
        var lastLossAmount = new int[1]; // closure-mutable holder

        var gainEffect = new Effect(
            $"{CardName}: you gain N life (N = life an opponent just lost)",
            () =>
            {
                var amount = lastLossAmount[0];
                if (amount <= 0) return;
                if (card.Controller == null) return;
                if (card.Controller.HasLost) return; // CR 614 — can't gain after loss

                card.Controller.GainLife(amount);
            });

        var condition = new EventTriggerCondition<LifeChangedEvent>((e, _) =>
        {
            // "an opponent loses life" — event player is NOT the
            // controller, and the life total strictly decreased.
            if (ReferenceEquals(e.Player, card.Controller)) return false;
            if (e.NewLife >= e.PreviousLife) return false;
            lastLossAmount[0] = e.PreviousLife - e.NewLife;
            return true;
        });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { gainEffect },
            activeZones: new[] { Majik.Core.Zones.ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
