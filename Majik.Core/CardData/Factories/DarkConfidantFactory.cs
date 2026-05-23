using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dark Confidant (Ravnica, {1}{B}).
///
/// Creature — Human Wizard 2/1. Oracle text:
///   "At the beginning of your upkeep, reveal the top card of your library
///    and put that card into your hand. You lose life equal to its mana value."
///
/// ## Implemented (v1)
/// - 2/1 Human Wizard with mana cost {1}{B}.
/// - Upkeep triggered ability scoped to controller's own upkeep
///   (<see cref="Triggers.OnStepBegin"/> filtered to controller). On
///   resolution:
///     1. Peeks at the controller's top library card.
///     2. Emits a <see cref="CardRevealedEvent"/> from the library (so
///        portal/log subscribers can flash the revealed card).
///     3. Moves the card from Library → Hand.
///     4. Calls <see cref="Player.LoseLife"/> with the card's printed mana
///        value (CR 202.3b / 202.3c — the mana value of a card is computed
///        from its mana cost; {X} contributes 0 in the library so X-spells
///        bottom out at the non-X portion, matching paper).
/// - Empty-library guard: when the library is empty, the trigger no-ops on
///   the reveal/draw and the controller takes 0 life loss (the
///   "tried to draw from empty library" flag is set so SBA can resolve
///   loss on the next opportunity).
///
/// ## Deferred (v1 gaps)
/// - <b>Live wiring against <see cref="TriggerManager"/></b>: the single-arg
///   factory attaches the trigger to the card so structural tests (and the
///   <see cref="NamedCardFactory"/> dispatch path) see the ability shape,
///   but the trigger is not registered with a TriggerManager — fire it
///   manually in tests. The (owner, bus, triggers) overload registers the
///   trigger so an Upkeep <see cref="StepStartedEvent"/> automatically
///   places it on the stack.
/// - <b>Reveal duration</b>: per CR 701.16 the card stays revealed until
///   the effect that revealed it stops applying. v1 emits the event once
///   and immediately moves the card to hand; clients are expected to
///   render the reveal as a transient flash.
/// </summary>
public static class DarkConfidantFactory
{
    /// <summary>
    /// Construct Dark Confidant with no live bus / trigger-manager wiring.
    /// The upkeep trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>; tests fire it manually via
    /// <see cref="TriggeredAbility.IsTriggered"/> or by executing the
    /// effect directly. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Dark Confidant with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied, the upkeep trigger is
    /// registered so an Upkeep <see cref="StepStartedEvent"/> for the
    /// controller automatically places it on the stack.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Dark Confidant",
            manaCost: "{1}{B}",
            power: 2,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1, CR 500.4.
        //   "At the beginning of your upkeep, reveal the top card of your
        //    library and put that card into your hand. You lose life equal
        //    to its mana value."
        // Triggers.OnStepBegin filters StepStartedEvent on (Upkeep,
        // controller) so it only fires on the controller's own upkeeps.
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            "Dark Confidant: reveal top, draw, lose life equal to its mana value",
            () =>
            {
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 120.3 — drawing from empty library is noted by the
                    // SBA loop on the next pass. No life loss because nothing
                    // was actually drawn.
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }

                // CR 701.16 — reveal the card. We emit the event from the
                // Library zone (the card hasn't moved yet at the moment of
                // reveal).
                eventBus?.Publish(new CardRevealedEvent(
                    top, owner, ZoneType.Library, "Dark Confidant"));

                // Move Library → Hand.
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);

                // Life loss = printed mana value of the revealed card. {X}
                // in the library contributes 0 (CR 202.3b). ICard exposes
                // ManaCost as the raw oracle string; parse it once here.
                var mv = Majik.Core.ValueObjects.ManaCost
                    .Parse(top.ManaCost ?? string.Empty)
                    .TotalValue;
                if (mv > 0)
                {
                    owner.LoseLife(mv);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        // Live registration with TriggerManager so the bus actually surfaces
        // the trigger as pending when an Upkeep step starts.
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
