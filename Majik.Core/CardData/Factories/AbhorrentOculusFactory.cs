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
/// ## Manifest dread (CR 701.59)
///
/// The opponent-upkeep trigger now resolves real manifest dread via
/// <see cref="ManifestDreadEffect.Resolve(Majik.Core.Players.Player, ZoneService?)"/>:
/// look at the top two cards of the controller's library, manifest the
/// first as a face-down 2/2 <see cref="Majik.Core.Cards.ManifestedCreature"/>
/// wrapper on the battlefield, and put the second into the controller's
/// graveyard. The wrapper preserves a reference to the underlying card
/// so the granted "turn face up for its mana cost" activated ability
/// (CR 708.6) can swap the wrapper out for the printed creature on
/// resolution.
///
/// ## Deferred (v1 gaps — small)
///
/// - <b>Agent prompt for pick-one-of-two:</b> v1 deterministically
///   manifests the top-of-library card; the second goes to graveyard.
///   Future agent prompt (mirror of Brainstorm / Ponder pick loops)
///   will let the controller's agent pick which goes where.
/// - <b>Non-creature underlying card:</b> if the manifested card is not
///   a creature, no face-up ability is granted (CR 701.59c — "if it's
///   a creature card"). The face-down 2/2 stays face-down indefinitely;
///   this matches the printed rule.
///
/// CR rule references: 205.3m (Eye subtype), 601.2f (additional cost),
/// 603.1 / 500.4 (upkeep trigger), 702.9 (Flying), 701.59 (manifest
/// dread), 708.2 / 708.6 (face-down permanents + turn-face-up).
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
    /// Pass <paramref name="zones"/> for ZoneService-routed manifest
    /// dread (ETB / LTB triggers fire); otherwise raw-zone moves are
    /// used.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Optional trigger manager. When supplied,
    /// the opponent-upkeep trigger is registered so
    /// <see cref="StepStartedEvent"/>s on opponents' upkeeps surface
    /// the manifest-dread trigger on the stack automatically.</param>
    /// <param name="zones">Optional <see cref="ZoneService"/> for
    /// event-routed manifest dread resolution.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers = null,
        Majik.Core.Services.ZoneService? zones = null)
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

        // CR 701.59 — resolve manifest dread for the trigger's
        // controller (Oculus's controller, not the player whose upkeep
        // fired). Capture `card` so we read the live controller at
        // resolve time rather than at construction (handles control
        // changes between triggering + resolution).
        var capturedCard = card;
        var capturedZones = zones;
        var manifestDreadEffect = new Effect(
            $"{CardName}: manifest dread (CR 701.59)",
            () => ManifestDreadEffect.Resolve(
                capturedCard.Controller ?? owner,
                capturedZones));

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

}
