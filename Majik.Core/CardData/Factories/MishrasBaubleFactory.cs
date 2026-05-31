using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mishra's Bauble (Coldsnap, reprinted in Modern
/// Horizons 2). Artifact — {0}. Oracle text:
///
///   "{T}, Sacrifice this artifact: Look at the top card of target player's
///    library. Draw a card at the beginning of the next turn's upkeep."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {0}, owner/controller).
/// - <b>Activated ability</b>: {T} + Sacrifice (self) — wired via
///   <see cref="AdditionalCost"/>.Tap + .Sacrifice. The sacrifice cost is
///   <em>declared</em> on the ability; the engine's existing
///   <c>AdditionalCost.Pay</c> is currently a TODO for sacrifice (see
///   <see cref="AdditionalCost"/>), so the effect itself moves the bauble
///   to its owner's graveyard to keep test-visible behavior correct.
///   (Same trick used by other v1 factories whose costs need observable
///   side effects.)
/// - <b>Look-at-top</b>: information-only — implemented as a peek that
///   does not mutate the library. Targeting is auto: v1 picks the
///   controller (own library); when the targeting prompt system lands,
///   this becomes "any player's library."
/// - <b>Delayed trigger</b>: at the start of the next upkeep step, the
///   controller draws a card. Built as a
///   <see cref="DelayedTriggeredAbility"/> that fires on the first
///   matching <see cref="StepStartedEvent"/> (Upkeep) and auto-unregisters
///   (handled by <see cref="TriggerManager"/>). The Bauble must be
///   registered with a <see cref="TriggerManager"/> for the delayed
///   trigger to be active; the parameterless overload omits this and is
///   only suitable for tests that exercise card-shape, not the delayed
///   draw. Use <see cref="Create(Player, TriggerManager)"/> for the full
///   behavior.
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt for "target player's library"</b>: v1
///   auto-targets the controller. Awaits the agent-prompt targeting
///   system used by other v1 factories.
/// - <b>"Next turn" specifically</b>: the upkeep delayed trigger here
///   fires on the first Upkeep StepStartedEvent <em>after</em> the
///   activation, which is the next upkeep regardless of whose turn it
///   is. Strictly Rule 603.7c says "the next turn's upkeep" — same
///   behavior for the simple two-player case. Multi-player turn-skipping
///   semantics deferred.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move so behavior is
///   observable. Once <see cref="AdditionalCost.Pay"/> performs the
///   sacrifice itself the explicit move-to-graveyard can be removed.
/// </summary>
[CardName("Mishra's Bauble")]
public static class MishrasBaubleFactory
{
    /// <summary>
    /// Construct Mishra's Bauble. The delayed draw trigger is built but
    /// will not actually fire because no <see cref="TriggerManager"/> is
    /// available to register it. Suitable for card-shape tests only.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggerManager: null);

    /// <summary>
    /// Construct Mishra's Bauble fully wired: activating the {T}, Sacrifice
    /// ability registers the upkeep draw delayed trigger with
    /// <paramref name="triggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggerManager)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(owner);
        bauble.SetController(owner);

        var effect = new Effect(
            "Mishra's Bauble: look at top of library, register upkeep draw",
            () =>
            {
                // ----------------------------------------------------------
                // "Look at the top card of target player's library."
                // v1: auto-target the controller (no targeting prompt yet).
                // Pure information — does not move the card. We .FirstOrDefault
                // so empty libraries are a no-op rather than throwing.
                // ----------------------------------------------------------
                _ = owner.Zones.Library.GetCards().FirstOrDefault();

                // ----------------------------------------------------------
                // Sacrifice payment is currently a no-op stub in
                // AdditionalCost. Move the bauble to graveyard here so
                // the visible state matches the rules. When the engine's
                // sacrifice plumbing is real, remove this block.
                // ----------------------------------------------------------
                if (bauble.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(bauble);
                    owner.Zones.Graveyard.AddCard(bauble);
                    bauble.SetZone(ZoneType.Graveyard);
                }

                // ----------------------------------------------------------
                // "Draw a card at the beginning of the next turn's upkeep."
                // Build a one-shot delayed triggered ability that:
                //   - fires on the first StepStartedEvent (Upkeep) after
                //     activation,
                //   - draws one card for the controller on resolution.
                // TriggerManager auto-unregisters delayed triggers after
                // they fire (see TriggerManager.EvaluateTriggers).
                //
                // Rule 603.7 — delayed triggered abilities are active in
                // all zones, so the bauble being in the graveyard does
                // not deactivate them.
                // ----------------------------------------------------------
                if (triggerManager == null)
                {
                    return; // no registry — caller opted out of delayed draw
                }

                // The trigger should only fire on a *future* upkeep, not the
                // one we may be standing in right now. Snapshot the current
                // upkeep "instance" by capturing a flag that flips after the
                // first StepEnded(Upkeep) we observe; until then matches are
                // suppressed. Simpler: skip if the timestamp of the
                // StepStartedEvent corresponds to the activation's own
                // upkeep. We use a flag flipped by an EndedEvent listener;
                // but the simplest correct approximation — match the FIRST
                // Upkeep event seen with a timestamp strictly after this
                // ability was set up — is what we go with here.
                var activatedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
                DelayedTriggeredAbility? delayed = null;
                var drawEffect = new Effect(
                    "Mishra's Bauble: draw a card (delayed upkeep)",
                    () =>
                    {
                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null) return; // SBAs handle empty-library loss elsewhere
                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    });

                delayed = new DelayedTriggeredAbility(
                    source: bauble,
                    controller: owner,
                    condition: new EventTriggerCondition<StepStartedEvent>(
                        (e, _) => e.StepType == PhaseStateType.Upkeep
                                  && e.Timestamp > activatedAt),
                    effects: new IEffect[] { drawEffect });

                triggerManager.RegisterDelayed(delayed);
            });

        var ability = new ActivatedAbility(
            source: bauble,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(bauble),
                AdditionalCost.Sacrifice(bauble),
            },
            effects: new IEffect[] { effect });

        bauble.AddAbility(ability);
        return bauble;
    }
}
