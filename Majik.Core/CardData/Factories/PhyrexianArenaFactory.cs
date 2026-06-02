using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Arena (Apocalypse, {1}{B}{B}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "At the beginning of your upkeep, you draw a card and you lose 1 life."
///
/// ## Implemented (v1)
/// - Enchantment with mana cost {1}{B}{B}.
/// - Upkeep triggered ability scoped to the controller's own upkeep
///   (<see cref="Triggers.OnStepBegin"/> filtered to controller — CR 603.1,
///   CR 500.4). On resolution:
///     1. Draws the top card of the controller's library into hand
///        (Library → Hand, emitting a <see cref="CardDrawnEvent"/> so
///        portal/log subscribers see the draw — CR 120).
///     2. Calls <see cref="Player.LoseLife"/> with a flat 1. The life loss
///        is a separate part of the effect from the draw (CR 120.3), so it
///        is independent of what (or whether) a card was drawn — this is the
///        key difference from Dark Confidant, whose loss is the revealed
///        card's mana value.
/// - Empty-library guard: when the library is empty, the draw no-ops and the
///   "tried to draw from empty library" flag is set (CR 120.3 / 704.5b — the
///   SBA loop resolves the loss-on-empty-draw on the next pass). The flat
///   1-life loss STILL applies, because "you lose 1 life" is its own clause.
///
/// ## Deferred (v1 gaps)
/// - <b>Live wiring against <see cref="TriggerManager"/></b>: the single-arg
///   factory attaches the trigger to the card so structural tests (and the
///   <see cref="NamedCardFactory"/> dispatch path) see the ability shape, but
///   the trigger is not registered with a TriggerManager — fire it manually
///   in tests. The (owner, bus, triggers) overload registers the trigger so
///   an Upkeep <see cref="StepStartedEvent"/> automatically places it on the
///   stack.
///
/// Hand-built (not JSON-def) because the declarative card-definition union
/// does not yet expose a "beginning of your upkeep" trigger or a
/// "you lose N life" effect; the underlying engine primitives
/// (<see cref="Triggers.OnStepBegin"/>, library→hand draw,
/// <see cref="Player.LoseLife"/>) all already exist and are exercised by the
/// Dark Confidant analogue.
/// </summary>
[CardName("Phyrexian Arena")]
public static class PhyrexianArenaFactory
{
    /// <summary>
    /// Construct Phyrexian Arena with no live bus / trigger-manager wiring.
    /// The upkeep trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>; tests fire it manually. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Phyrexian Arena with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied, the upkeep trigger is
    /// registered so an Upkeep <see cref="StepStartedEvent"/> for the
    /// controller automatically places it on the stack.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            name: "Phyrexian Arena",
            manaCost: "{1}{B}{B}");

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1, CR 500.4.
        //   "At the beginning of your upkeep, you draw a card and you lose
        //    1 life."
        // Triggers.OnStepBegin filters StepStartedEvent on (Upkeep,
        // controller) so it only fires on the controller's own upkeeps.
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            "Phyrexian Arena: draw a card and lose 1 life",
            () =>
            {
                // Draw a card: Library → Hand (CR 120). Empty-library guard
                // mirrors the canonical draw effect — the SBA loop notes the
                // attempt and resolves the loss-on-empty-draw separately.
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                }
                else
                {
                    owner.Zones.Library.RemoveCard(top);
                    owner.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                    eventBus?.Publish(new CardDrawnEvent(top, owner));
                }

                // "you lose 1 life" is its own clause (CR 120.3), independent
                // of whether a card was actually drawn — flat 1, always.
                owner.LoseLife(1);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        // Live registration with TriggerManager so the bus surfaces the
        // trigger as pending when an Upkeep step starts.
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
