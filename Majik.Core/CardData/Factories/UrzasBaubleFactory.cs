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
/// Named-card factory for Urza's Bauble (Antiquities, reprinted in Modern
/// Horizons 2). Artifact — {0}. Oracle text:
///
///   "{T}, Sacrifice Urza's Bauble: Look at a random card in target
///    player's hand. Draw a card at the beginning of the next turn's
///    upkeep."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {0}, owner/controller). Sister
///   card to Mishra's Bauble — same shape modulo the look-at-target:
///   Mishra's Bauble peeks at the top of a library, Urza's Bauble peeks
///   at a random hand card.
/// - <b>Activated ability</b>: {T} + Sacrifice (self) — wired via
///   <see cref="AdditionalCost"/>.Tap + .Sacrifice. The sacrifice cost
///   is <em>declared</em> on the ability; the engine's existing
///   <c>AdditionalCost.Pay</c> is currently a TODO for sacrifice (see
///   <see cref="AdditionalCost"/>), so the effect itself moves the
///   bauble to its owner's graveyard to keep test-visible behavior
///   correct. Same trick as <see cref="MishrasBaubleFactory"/>.
/// - <b>Look-at-random-hand-card</b>: information-only — implemented as
///   a peek that does not mutate the targeted player's hand. v1 auto-
///   targets the controller (own hand) — same posture as
///   <see cref="MishrasBaubleFactory"/>'s auto-self-library peek. When
///   the targeting prompt system lands, this becomes "any player's hand."
/// - <b>Delayed trigger</b>: at the start of the next upkeep step, the
///   controller draws a card. Built as a
///   <see cref="DelayedTriggeredAbility"/> identical in shape to
///   Mishra's Bauble's. The Bauble must be registered with a
///   <see cref="TriggerManager"/> for the delayed trigger to be active;
///   the parameterless overload omits this and is only suitable for
///   tests that exercise card-shape, not the delayed draw. Use
///   <see cref="Create(Player, TriggerManager)"/> for the full behavior.
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt for "target player's hand"</b>: v1 auto-
///   targets the controller. Awaits the agent-prompt targeting system
///   used by other v1 factories (same gap as Mishra's Bauble).
/// - <b>Random-card selection</b>: the look-at half is information-only
///   and the engine doesn't currently surface a "reveal a random card"
///   primitive to the bot; the peek is collapsed to a deterministic
///   first-card-in-hand sample (no mutation). Will replace once a
///   random-pick prompt lands.
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
[CardName("Urza's Bauble")]
public static class UrzasBaubleFactory
{
    public const string CardName = "Urza's Bauble";
    public const string PrintedManaCost = "{0}";

    /// <summary>
    /// Construct Urza's Bauble. The delayed draw trigger is built but
    /// will not actually fire because no <see cref="TriggerManager"/> is
    /// available to register it. Suitable for card-shape tests only.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggerManager: null);

    /// <summary>
    /// Construct Urza's Bauble fully wired: activating the {T}, Sacrifice
    /// ability registers the upkeep draw delayed trigger with
    /// <paramref name="triggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggerManager)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var bauble = new Artifact(CardName, PrintedManaCost);
        bauble.SetOwner(owner);
        bauble.SetController(owner);

        var effect = new Effect(
            $"{CardName}: look at random hand card, register upkeep draw",
            () =>
            {
                // ----------------------------------------------------------
                // "Look at a random card in target player's hand."
                // v1: auto-target the controller (no targeting prompt yet).
                // Pure information — does not move the card. We
                // .FirstOrDefault as a deterministic stand-in for "random"
                // (real random-pick prompt deferred — see class xmldoc).
                // ----------------------------------------------------------
                _ = owner.Zones.Hand.GetCards().FirstOrDefault();

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
                // One-shot DelayedTriggeredAbility — identical wiring to
                // Mishra's Bauble.
                // Rule 603.7 — delayed triggered abilities are active in
                // all zones, so the bauble being in the graveyard does
                // not deactivate them.
                // ----------------------------------------------------------
                if (triggerManager == null) return;

                var activatedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
                var drawEffect = new Effect(
                    $"{CardName}: draw a card (delayed upkeep)",
                    () =>
                    {
                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null) return; // SBAs handle empty-library elsewhere
                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    });

                var delayed = new DelayedTriggeredAbility(
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
