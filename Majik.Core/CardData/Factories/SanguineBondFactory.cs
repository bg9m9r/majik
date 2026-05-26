using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sanguine Bond (Magic 2010, {4}{B}{B}).
///
/// Enchantment. Oracle text:
///   "Whenever you gain life, target opponent loses that much life."
///
/// ## Implemented (v1)
/// - Card identity (Enchantment {4}{B}{B}, owner / controller wiring).
/// - <b>Lifegain → opponent-drain triggered ability (CR 119.3 / 603.6a)</b>:
///   Wired via <see cref="Triggers.OnLifeGainedByPlayer"/> consuming
///   <see cref="LifeChangedEvent"/> (filtered to Sanguine Bond's controller
///   AND strictly-positive deltas — life *gain*, not life loss). The amount
///   gained is captured at trigger time via the
///   <see cref="EventBindableTriggeredAbility"/> wiring so the resolved
///   effect knows how much life to drain (CR 603.2a — values determined at
///   trigger time). On resolution the chosen target opponent loses N life
///   where N = (NewLife − PreviousLife) of the original gain event.
/// - <b>1..1 "target opponent" target request</b> mirroring
///   <see cref="TendrilsOfAgonyFactory"/> — bot picks an opponent and the
///   life-loss half no-ops on illegal-on-resolve (CR 608.2b) without
///   crashing.
///
/// ## Combo interaction (Exquisite Blood)
/// Sanguine Bond + Exquisite Blood is the canonical infinite drain combo.
/// Trigger flow: any 1+ life gain by Sanguine Bond's controller
///   → Sanguine Bond triggers, opponent loses N
///   → Exquisite Blood (controlled by same player) triggers on opponent
///     life loss, controller gains N
///   → Sanguine Bond triggers again, ...
/// The engine does NOT short-circuit the loop structurally; SBAs handle
/// termination once the opponent's life total hits 0 (CR 704.5a — player
/// with 0 life loses the game; once <see cref="Player.HasLost"/> is set,
/// subsequent <see cref="Player.LoseLife"/> calls throw and the trigger
/// chain unwinds). The stack-resolution loop in <c>StackResolver</c> keeps
/// pushing successive triggers until the opponent loses.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven target prompt</b>: the trigger honours pre-set
///   <see cref="ITriggeredAbility.ChosenTargets"/>; the factory does NOT
///   wire an <see cref="IPlayerAgent"/> prompt. Tests call
///   <see cref="TriggeredAbility.SetChosenTargets"/> directly (same
///   posture as Heliod, Sun-Crowned / Earthshaker Khenra).
/// - <b>"Target opponent" choose-time filtering</b>: enforced at resolve
///   via runtime type-check; choose-time candidate gathering is deferred
///   to the broader <see cref="TargetRequest.LegalCandidates"/> plumbing.
/// </summary>
[CardName("Sanguine Bond")]
public static class SanguineBondFactory
{
    public const string CardName = "Sanguine Bond";
    public const string PrintedManaCost = "{4}{B}{B}";

    /// <summary>
    /// Construct Sanguine Bond. The lifegain trigger is attached to the
    /// card shape but not registered with a <see cref="TriggerManager"/>.
    /// Suitable for card-shape / dispatcher tests — tests fire the
    /// triggered ability by invoking its effect directly.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Sanguine Bond with the lifegain trigger registered against
    /// <paramref name="triggers"/> when supplied. The captured gain amount
    /// is read off the event at trigger time and threaded through to the
    /// resolved effect via a per-trigger mutable closure.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Lifegain → opponent-drain triggered ability — CR 119.3 / 603.6a.
        //   "Whenever you gain life, target opponent loses that much life."
        // Captures the gain delta at trigger time (CR 603.2a) via a
        // mutable last-amount holder that the LifeChangedEvent matcher
        // writes through. The resolved effect reads it and applies the
        // drain to the chosen target opponent.
        // ----------------------------------------------------------------
        var lastGainAmount = new int[1]; // closure-mutable holder

        TriggeredAbility? trigger = null;

        var drainEffect = new Effect(
            $"{CardName}: target opponent loses N life (N = life just gained)",
            () =>
            {
                if (trigger == null) return;
                var amount = lastGainAmount[0];
                if (amount <= 0) return;

                var chosen = trigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Player target) return;
                if (ReferenceEquals(target, card.Controller)) return; // opponent only
                if (target.HasLost) return; // CR 608.2b — already lost

                target.LoseLife(amount);
            });

        // Custom event matcher that writes the gain amount into the holder
        // before delegating to the standard "you gained life" predicate.
        var condition = new EventTriggerCondition<LifeChangedEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Player, card.Controller)) return false;
            if (e.NewLife <= e.PreviousLife) return false;
            lastGainAmount[0] = e.NewLife - e.PreviousLife;
            return true;
        });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { Majik.Core.Zones.ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
