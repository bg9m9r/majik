using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Delver of Secrets — DFC front face (Innistrad,
/// {U}). Back face: Insectile Aberration (3/2 Flying).
///
/// Creature — Human Wizard 1/1. Oracle text (front):
///   "At the beginning of your upkeep, look at the top card of your library.
///    You may reveal that card. If an instant or sorcery card is revealed
///    this way, transform Delver of Secrets."
///
/// Back face (Insectile Aberration): Creature — Human Insect 3/2, Flying.
/// The back-face P/T + Flying is NOT swapped onto the live Creature object
/// in v1 — Layer 0 / per-face characteristic replacement is deferred. The
/// transform is observed by flipping the attached <see cref="MdfcState"/>;
/// callers / tests inspect <see cref="MdfcState.IsBackFace"/> to determine
/// the active face.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Wizard at {U}, owner / controller set.
/// - <see cref="MdfcState"/> attached with front = "Delver of Secrets",
///   back = "Insectile Aberration" (CR 711).
/// - Upkeep triggered ability (CR 603.1 / CR 500.4) scoped to controller's
///   own upkeep via <see cref="Triggers.OnStepBegin"/>. On resolution:
///     1. Peek the top of the controller's library (no zone move — CR 701.19
///        "look at" reveals only to the controller).
///     2. Emit a <see cref="CardRevealedEvent"/> from the library so portal
///        / log subscribers can flash the revealed card. The "you may
///        reveal" is auto-accepted when the top card is an instant or
///        sorcery (deterministic v1 policy — same queue as every other "you
///        may" deferral; revealing a non-trigger card is irrelevant since
///        only instants/sorceries flip the transform).
///     3. If the peeked card has type Instant or Sorcery, flip the
///        <see cref="MdfcState"/> to its back face (CR 701.28 — transform).
/// - Empty-library guard: when the library is empty, the trigger no-ops on
///   the peek and the controller does NOT take a "tried to draw from empty
///   library" hit — looking at the top of an empty library is a no-op (CR
///   701.19), not a draw (CR 120.3).
///
/// ## Deferred (v1 gaps)
/// - <b>Back-face hot-swap on the battlefield.</b> The transform only flips
///   the MdfcState; the Creature object remains a 1/1 Human Wizard without
///   Flying. A full DFC characteristic-replacement (replace with a 3/2
///   Human Insect with Flying) would require Layer 0 / per-face cardpool
///   support that the engine does not yet have. Same v1 limit as Ajani,
///   Nacatl Pariah.
/// - <b>"You may" prompt.</b> Auto-reveals when the peeked card is an
///   instant or sorcery; non-instant/sorcery peeks skip the reveal event.
///   A real agent-driven yes/no prompt is deferred — same queue as Sun
///   Titan / Ajani / Stoneforge Mystic.
/// - <b>Live wiring against <see cref="TriggerManager"/></b>: the single-arg
///   factory attaches the trigger to the card so structural tests (and the
///   <see cref="NamedCardFactory"/> dispatch path) see the ability shape,
///   but the trigger is not registered with a TriggerManager — fire it
///   manually in tests. The (owner, bus, triggers) overload registers the
///   trigger so an Upkeep <see cref="StepStartedEvent"/> automatically
///   places it on the stack.
/// </summary>
[CardName("Delver of Secrets")]
public static class DelverOfSecretsFactory
{
    public const string FrontName = "Delver of Secrets";
    public const string BackName = "Insectile Aberration";
    public const string FrontCost = "{U}";

    /// <summary>
    /// Construct Delver of Secrets with no live bus / trigger-manager wiring.
    /// The upkeep peek trigger is attached to the card but not registered
    /// with a <see cref="TriggerManager"/>; tests fire it manually via
    /// <see cref="TriggeredAbility.IsTriggered"/> or by executing the
    /// effect directly. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Delver of Secrets with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied, the upkeep trigger is
    /// registered so an Upkeep <see cref="StepStartedEvent"/> for the
    /// controller automatically places it on the stack.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: FrontName,
            manaCost: FrontCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 — attach the DFC face-tracker so callers can observe the
        // active face. Starts on the front face (Delver of Secrets);
        // Transform() flips IsBackFace.
        card.MdfcState = new MdfcState(FrontName, BackName);

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1, CR 500.4.
        //   "At the beginning of your upkeep, look at the top card of your
        //    library. You may reveal that card. If an instant or sorcery
        //    card is revealed this way, transform Delver of Secrets."
        //
        // Triggers.OnStepBegin filters StepStartedEvent on (Upkeep,
        // controller) so it only fires on the controller's own upkeeps.
        //
        // Resolution:
        //   1. Peek the top of the controller's library — no zone move
        //      (CR 701.19 "look at").
        //   2. If the top is instant or sorcery, emit a CardRevealedEvent
        //      and flip MdfcState to the back face (CR 701.28).
        //   3. If the top is some other card, the peek is silent (the
        //      controller still knows what it is, but no public reveal
        //      and no transform).
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            $"{FrontName}: upkeep peek; transform if instant/sorcery revealed",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.MdfcState == null || card.MdfcState.IsBackFace) return;

                var controller = card.Controller ?? owner;
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;  // CR 701.19 — looking at empty library is a no-op.

                var isInstantOrSorcery =
                    top.HasType(CardType.Instant) || top.HasType(CardType.Sorcery);
                if (!isInstantOrSorcery) return;

                // CR 701.16 — reveal the card to all players. The card
                // stays on top of the library; no zone move.
                eventBus?.Publish(new CardRevealedEvent(
                    top, controller, ZoneType.Library, FrontName));

                // CR 701.28 — transform. Flip the MdfcState to the back
                // face (Insectile Aberration). v1 does not hot-swap P/T
                // or Flying onto the live Creature object — see "Deferred"
                // section in the class doc.
                card.MdfcState.Transform();
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
