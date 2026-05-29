using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tamiyo, Inquisitive Student // Tamiyo, Seasoned
/// Scholar — Transform DFC front face (Modern Horizons 3, {U}).
///
/// Front face — Tamiyo, Inquisitive Student. Legendary Creature — Moonfolk
/// Wizard, 0/3. Oracle text:
///   "Flying
///    Whenever Tamiyo attacks, investigate. (Create a Clue token. It's an
///    artifact with \"{2}, Sacrifice this token: Draw a card.\")
///    When you draw your third card in a turn, exile Tamiyo, then return her
///    to the battlefield transformed under her owner's control."
///
/// Back face — Tamiyo, Seasoned Scholar. Legendary Planeswalker — Tamiyo,
/// loyalty 2. The back-face loyalty abilities are NOT modelled by this
/// factory — only the DFC plumbing (front-face shape + the two front-face
/// triggers + the transform flip). This mirrors the
/// <see cref="AjaniNacatlPariahFactory"/> posture exactly (a creature front
/// face transforming into a planeswalker back face).
///
/// ## Implemented (v1)
/// - 0/3 Creature — Moonfolk Wizard at {U}, owner / controller set.
/// - <see cref="KeywordAbility"/> Flying marker (CR 702.9), consumed by the
///   combat subsystem (CombatAbilities.HasFlying).
/// - <see cref="MdfcState"/> attached, starting on the front face. The
///   transform trigger flips it to the back face (Tamiyo, Seasoned Scholar)
///   — same observation surface as Ajani, Nacatl Pariah.
/// - <b>Attack trigger → investigate</b> (CR 508.1f attack trigger +
///   CR 701.30 investigate): <see cref="Triggers.OnAttackSelf"/> over
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>;
///   resolves by creating one Clue token under the controller via
///   <see cref="TokenFactory.CreateClue"/> (routed through the supplied
///   <see cref="ZoneService"/> so the Clue's ETB CardMovedEvent fires).
/// - <b>"Draw your third card in a turn" trigger → transform</b>
///   (CR 603.2 reflexive draw-count trigger + CR 701.28 transform). The
///   factory maintains a per-turn draw counter: each
///   <see cref="CardDrawnEvent"/> for the owner increments it, and the
///   trigger fires on the rising edge of the third draw (count == 3). The
///   count resets on <see cref="TurnStartedEvent"/>. On resolution the
///   attached <see cref="MdfcState"/> flips to the back face.
///
/// ## Deferred (v1 gaps)
/// - <b>Exile-then-return-transformed.</b> The printed text exiles Tamiyo
///   and returns her transformed (a zone round-trip that resets her as a
///   "new object" — CR 701.28b). v1 flips the MdfcState in place (no
///   exile/return), matching the Ajani, Nacatl Pariah transform posture.
///   A true exile + return would require the same Layer-0 / per-face
///   hot-swap that DFC permanents still lack (see Ajani deferral note).
/// - <b>Back-face loyalty abilities + planeswalker body.</b> Tamiyo,
///   Seasoned Scholar's [+2] / [-3] / [-7] loyalty abilities and the
///   Planeswalker characteristics (loyalty 2) are not wired. The back face
///   is shape-only tracked through MdfcState.BackFaceName — identical to
///   Ajani, Nacatl Avenger.
/// - <b>"Third card" across draw replacements.</b> The counter watches
///   <see cref="CardDrawnEvent"/>; cards put into hand by non-draw effects
///   (e.g. "put into your hand") correctly do NOT count, matching the
///   printed "draw your third card" wording (CR 120 draw definition).
/// </summary>
[CardName("Tamiyo, Inquisitive Student // Tamiyo, Seasoned Scholar")]
public static class TamiyoInquisitiveStudentFactory
{
    public const string FrontName = "Tamiyo, Inquisitive Student";
    public const string BackName = "Tamiyo, Seasoned Scholar";
    public const string FrontCost = "{U}";

    /// <summary>
    /// Construct Tamiyo with no live ZoneService / TriggerManager / EventBus
    /// wiring (shape / dispatcher path). The Flying keyword, attack→investigate
    /// trigger, and draw-third→transform trigger are attached to the card so
    /// structural assertions still see them; nothing is registered with a
    /// manager and Clue tokens bypass ZoneService.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Tamiyo, Inquisitive Student with optional live wiring.
    /// When <paramref name="triggers"/> is supplied both front-face triggers
    /// are registered so the runtime queues them on the appropriate events.
    /// When <paramref name="zoneService"/> is supplied the investigate Clue
    /// is placed via ZoneService so its ETB CardMovedEvent fires. When
    /// <paramref name="eventBus"/> is supplied the per-turn draw counter is
    /// driven from the live bus (increment on <see cref="CardDrawnEvent"/>,
    /// reset on <see cref="TurnStartedEvent"/>).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        EventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: FrontName,
            manaCost: FrontCost,
            power: 0,
            toughness: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Moonfolk, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 — DFC face tracker. Starts on the front face (Tamiyo,
        // Inquisitive Student); the draw-third trigger flips IsBackFace.
        card.MdfcState = new MdfcState(FrontName, BackName);

        // CR 702.9 — Flying. KeywordAbility marker consumed by the combat
        // subsystem (CombatAbilities.HasFlying / blocking legality).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        AttachAttackInvestigateTrigger(card, owner, zoneService, triggers);
        AttachDrawThirdTransformTrigger(card, owner, triggers, eventBus);

        return card;
    }

    /// <summary>
    /// "Whenever Tamiyo attacks, investigate." (CR 508.1f attack trigger +
    /// CR 701.30 investigate — create one Clue token under the controller.)
    /// </summary>
    private static void AttachAttackInvestigateTrigger(
        Creature card,
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        var investigateEffect = new Effect(
            $"{FrontName}: attack → investigate (create a Clue)",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateClue(controller, zoneService);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { investigateEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    /// <summary>
    /// "When you draw your third card in a turn, exile Tamiyo, then return
    /// her to the battlefield transformed under her owner's control."
    ///
    /// CR 603.2 — reflexive draw-count trigger. A per-turn counter is
    /// incremented on each <see cref="CardDrawnEvent"/> for the owner and
    /// reset on <see cref="TurnStartedEvent"/>. The trigger condition fires
    /// on the rising edge of the third draw (count transitions to 3).
    /// Resolution flips the attached <see cref="MdfcState"/> to the back
    /// face (CR 701.28) — the exile/return zone round-trip is deferred (see
    /// class remarks).
    /// </summary>
    private static void AttachDrawThirdTransformTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers,
        EventBus? eventBus)
    {
        // Per-turn draw count. Boxed in a single-element array so the bus
        // subscriptions and the trigger predicate share one mutable cell.
        var drawsThisTurn = new int[1];

        // Live-bus path: drive the counter from the real event stream so the
        // count is accurate regardless of how many handlers evaluate the
        // trigger predicate. Reset at the start of EVERY turn (CR 500.1 —
        // "in a turn" spans the whole turn, not just the owner's turn; a
        // card can be drawn on the opponent's turn too).
        eventBus?.Subscribe<TurnStartedEvent>(_ => drawsThisTurn[0] = 0);
        eventBus?.Subscribe<CardDrawnEvent>(e =>
        {
            if (ReferenceEquals(e.Player, owner))
            {
                drawsThisTurn[0]++;
            }
        });

        // Trigger condition: fire on the rising edge of the third draw.
        // When no live bus is wired (shape path) we fall back to counting in
        // the predicate itself so the trigger still behaves correctly under
        // a bare TriggerManager. EvaluateTriggers calls Matches exactly once
        // per published CardDrawnEvent (TriggerManager.EvaluateTriggers).
        var condition = new EventTriggerCondition<CardDrawnEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Player, owner)) return false;
            // When a live bus drives the counter, the CardDrawnEvent
            // subscription above has already incremented it before the
            // TriggerManager's global handler evaluates this predicate
            // (subscriptions fire in registration order; the factory wires
            // its counter before the trigger is registered). Fire only on
            // the third draw.
            if (eventBus != null)
            {
                return drawsThisTurn[0] == 3;
            }
            // Shape / bare-manager fallback: count here.
            drawsThisTurn[0]++;
            return drawsThisTurn[0] == 3;
        });

        var transformEffect = new Effect(
            $"{FrontName}: draw-third → transform",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.MdfcState == null || card.MdfcState.IsBackFace) return;
                // CR 701.28 — transform. The exile/return round-trip is
                // deferred; v1 flips the face in place (Ajani posture).
                card.MdfcState.Transform();
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { transformEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }
}
