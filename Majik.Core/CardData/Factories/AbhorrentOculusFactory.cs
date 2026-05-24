using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Abhorrent Oculus (Duskmourn: House of Horror,
/// {2}{U}).
///
/// Creature — Eye 5/5. Oracle text (Scryfall, 2024-09-27):
///   "As an additional cost to cast this spell, exile six cards from
///    your graveyard.
///    Flying
///    At the beginning of each opponent's upkeep, manifest dread.
///    (Look at the top two cards of your library. Put one onto the
///    battlefield face down as a 2/2 creature and the other into your
///    graveyard. Turn it face up any time for its mana cost if it's a
///    creature card.)"
///
/// ## Implemented (v1)
///
/// - <b>Creature — Eye {2}{U} 5/5</b>. Introduces <see cref="CardSubtype.Eye"/>.
/// - <b>Flying</b> (CR 702.9) wired as a <see cref="KeywordAbility"/>
///   marker, same shape as <see cref="MantisRiderFactory"/> /
///   <see cref="SpriteDragonFactory"/>.
/// - <b>Additional cost — exile six cards from your graveyard
///   (CR 601.2f)</b>: surfaced via
///   <see cref="BuildExileSixCardsAdditionalCost"/> →
///   <see cref="ExileCardsFromGraveyardAdditionalCost"/>. Unlike
///   <see cref="HogaakFactory"/>'s exile-two-creatures cost, this one
///   accepts cards of any type — the printed oracle says "exile six
///   cards" with no creature gate.
/// - <b>"At the beginning of each opponent's upkeep" trigger
///   (CR 603.1 / CR 500.4)</b>: wired via a raw
///   <see cref="EventTriggerCondition{T}"/> over
///   <see cref="StepStartedEvent"/> filtered to
///   <c>StepType == Upkeep</c> AND <c>Player != controller</c>
///   (controller's own upkeeps are excluded; symmetric players in
///   future multiplayer setups all fire independently — same shape as
///   Sheoldred's draw-trigger controller filter, inverted).
///
/// ## Deferred (v1 gaps — manifest dread is a stub)
///
/// - <b>Manifest dread</b> (CR 701.59 / Duskmourn): the printed
///   resolution effect — look at top two of library, put one onto the
///   battlefield face down as a 2/2 manifest creature and the other
///   into your graveyard — is wired as a structural stub. The
///   triggered ability's body executes a no-op
///   <see cref="ManifestDreadStub"/> effect that documents the gap;
///   no library reveal, no face-down 2/2 token, no graveyard placement
///   happens at v1. Blockers: (a) no face-down / morph / manifest
///   primitive on the engine yet (no `Permanent.IsFaceDown` flag, no
///   "turn face up for mana cost" activated ability); (b) no agent-
///   side "look at N cards and choose which goes to battlefield vs
///   graveyard" prompt — same queue as Brainstorm / Ponder's pick
///   loops. The trigger itself is fully wired so once manifest dread
///   becomes a real primitive, only the effect body needs to swap to
///   a real implementation.
/// - <b>"Turn it face up any time for its mana cost if it's a
///   creature card"</b>: dependent on the manifest dread primitive
///   above. Not implemented.
///
/// CR rule references: 205.3m (Eye subtype), 601.2f (additional cost),
/// 603.1 / 500.4 (upkeep trigger), 702.9 (Flying), 701.59 (manifest
/// dread — deferred).
/// </summary>
[CardName("Abhorrent Oculus")]
public static class AbhorrentOculusFactory
{
    public const string CardName = "Abhorrent Oculus";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 5;
    public const int Toughness = 5;
    public const int ExileCostCount = 6;

    /// <summary>
    /// Construct Abhorrent Oculus owned and controlled by
    /// <paramref name="owner"/>. The Flying keyword marker is always
    /// wired. The opponent-upkeep trigger is attached to the card
    /// shape; pass <paramref name="triggers"/> to register it with the
    /// supplied <see cref="TriggerManager"/> for bus-driven firing.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Optional trigger manager. When supplied,
    /// the opponent-upkeep trigger is registered so
    /// <see cref="StepStartedEvent"/>s on opponents' upkeeps surface
    /// the manifest-dread (stub) trigger on the stack automatically.</param>
    public static Creature Create(Player owner, TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eye });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.9 — Flying. KeywordAbility marker consumed by
        // Majik.Core.Combat.CombatAbilities.HasFlying.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Opponent-upkeep trigger — CR 603.1 / CR 500.4.
        //   "At the beginning of each opponent's upkeep, manifest dread."
        // Predicate: StepStartedEvent with StepType == Upkeep AND
        // Player != controller. Distinct from Triggers.OnStepBegin
        // which gates to the controller's own upkeeps. The body runs
        // the manifest-dread stub — see class doc for the deferral
        // rationale.
        // ----------------------------------------------------------------
        var opponentUpkeepCondition = new EventTriggerCondition<StepStartedEvent>(
            (e, _) =>
                e.StepType == PhaseStateType.Upkeep
                && !ReferenceEquals(e.Player, card.Controller ?? owner));

        var manifestDreadEffect = new Effect(
            $"{CardName}: manifest dread (v1 stub — see factory xmldoc)",
            ManifestDreadStub);

        var opponentUpkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: opponentUpkeepCondition,
            effects: new IEffect[] { manifestDreadEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(opponentUpkeepTrigger);
        triggers?.RegisterTriggeredAbility(opponentUpkeepTrigger);

        return card;
    }

    /// <summary>
    /// Build the printed additional cost (CR 601.2f) — "exile six cards
    /// from your graveyard." Returned as an <see cref="IAdditionalCost"/>
    /// so <see cref="Majik.Core.Services.SpellCastFlow"/> can compose
    /// it into the cast pipeline. Mirrors
    /// <see cref="HogaakFactory.BuildExileTwoCreaturesAdditionalCost"/>'s
    /// pattern.
    /// </summary>
    public static ExileCardsFromGraveyardAdditionalCost
        BuildExileSixCardsAdditionalCost() => new(count: ExileCostCount);

    /// <summary>
    /// v1 manifest dread placeholder. Documented as a no-op so the
    /// triggered ability has an observable resolution shape today (the
    /// stack push + resolve path still exercises the trigger plumbing).
    /// Swap for a real manifest-dread effect once the primitive ships.
    /// </summary>
    internal static void ManifestDreadStub()
    {
        // CR 701.59 — manifest dread. Real implementation:
        //   1. Look at the top two cards of the controller's library.
        //   2. Agent picks one to manifest (face-down 2/2 creature
        //      token on battlefield) and the other to put into the
        //      graveyard.
        //   3. The manifested permanent can be turned face up at any
        //      time by paying its mana cost if it's a creature card
        //      (CR 701.59c, CR 702.36 morph-like rules).
        // Blocked on: face-down / manifest primitives + agent-side
        // pick-one-of-two prompt. v1 is intentionally a no-op so the
        // trigger fires + resolves cleanly without observable side
        // effects.
    }
}
