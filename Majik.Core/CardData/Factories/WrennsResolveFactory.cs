using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wrenn's Resolve (Murders at Karlov Manor, {R}).
///
/// Sorcery. Oracle text:
///   "Draw two cards. Exile cards drawn this way at the next end step."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {R}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) draws two cards
///   sequentially. Each drawn card is captured in a closure-local list and,
///   if a <see cref="TriggerManager"/> is supplied, a one-shot
///   <see cref="DelayedTriggeredAbility"/> is registered that fires on the
///   first end-step <see cref="StepStartedEvent"/> after this resolve.
///   The trigger exiles any captured cards still in the controller's hand
///   (cards played, discarded, or otherwise moved elsewhere are skipped —
///   "cards drawn this way" tracks the card identity, not its current zone).
/// - Empty library: draws what's available and flags the player for the
///   SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
///
/// ## Deferred (v1 gaps)
/// - Same single-step "next end step" approximation as Mishra's Bauble's
///   delayed-upkeep draw: the trigger fires on the first End step seen with
///   a timestamp strictly after this resolve. For the standard two-player
///   case this matches CR 603.7c. Multi-player turn-skipping nuances
///   deferred.
/// </summary>
[CardName("Wrenn's Resolve")]
public static class WrennsResolveFactory
{
    public const string CardName = "Wrenn's Resolve";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Build a Wrenn's Resolve sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Wrenn's Resolve's resolve effect — draw two cards and (when a
    /// <see cref="TriggerManager"/> is supplied) register a delayed
    /// "exile these at the next end step" rider. The trigger only exiles
    /// captured cards that are still in <paramref name="caster"/>'s hand
    /// at end-step resolution; cards already played, discarded, or
    /// otherwise relocated are left alone.
    /// </summary>
    /// <param name="caster">The player drawing + tagged-cards owner.</param>
    /// <param name="triggers">
    /// Optional trigger manager. When null the draw still happens, but the
    /// exile-at-EOT rider is skipped (suitable for shape tests).
    /// </param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Wrenn's Resolve: draw two cards, exile them at next end step.", () =>
            {
                // ----------------------------------------------------------
                // CR 121.1 — "Draw two cards." Two simple top-of-library
                // draws. Empty library mid-draw flags the player for the
                // SBA loss (CR 704.5b) and short-circuits the remaining
                // draws. Cards actually drawn are captured for the rider.
                // ----------------------------------------------------------
                var drawn = new List<ICard>(2);
                for (var i = 0; i < 2; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                    drawn.Add(top);
                }

                if (drawn.Count == 0 || triggers == null)
                {
                    return;
                }

                // ----------------------------------------------------------
                // "Exile cards drawn this way at the next end step." Build
                // a one-shot DelayedTriggeredAbility (CR 603.7) that:
                //   - fires on the first StepStartedEvent(End) seen with a
                //     timestamp strictly after this resolve (mirrors the
                //     activation-time fence used by MishrasBaubleFactory),
                //   - exiles any captured cards still in the caster's hand.
                // TriggerManager auto-unregisters delayed triggers after
                // they fire (see TriggerManager.EvaluateTriggers).
                // ----------------------------------------------------------
                var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
                var exileEffect = new Effect(
                    "Wrenn's Resolve: exile cards drawn this way (delayed end step)",
                    () =>
                    {
                        foreach (var c in drawn)
                        {
                            // Only exile cards that are still in the
                            // caster's hand — "cards drawn this way" tracks
                            // card identity, not their current zone, so a
                            // played / discarded / cycled card is no longer
                            // an exile target.
                            if (c.Zone != ZoneType.Hand) continue;
                            if (!caster.Zones.Hand.GetCards().Contains(c)) continue;

                            caster.Zones.Hand.RemoveCard(c);
                            caster.Zones.Exile.AddCard(c);
                            c.SetZone(ZoneType.Exile);
                        }
                    });

                var delayed = new DelayedTriggeredAbility(
                    source: caster,
                    controller: caster,
                    condition: new EventTriggerCondition<StepStartedEvent>(
                        (e, _) => e.StepType == PhaseStateType.End
                                  && e.Timestamp > resolvedAt),
                    effects: new IEffect[] { exileEffect });

                triggers.RegisterDelayed(delayed);
            }),
        };
    }
}
