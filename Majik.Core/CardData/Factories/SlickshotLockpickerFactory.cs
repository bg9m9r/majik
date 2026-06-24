using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Slickshot Lockpicker (Outlaws of Thunder Junction,
/// {2}{U}). Creature — Human Rogue 2/3. Oracle text (verified against Scryfall):
///   "When this creature enters, target instant or sorcery card in your
///    graveyard gains flashback until end of turn. The flashback cost is equal
///    to its mana cost. (You may cast that card from your graveyard for its
///    flashback cost. Then exile it.)
///    Plot {2}{U} (You may pay {2}{U} and exile this card from your hand. Cast
///    it as a sorcery on a later turn without paying its mana cost. Plot only
///    as a sorcery.)"
///
/// ## Implemented (v1)
/// The ETB grant is mechanically identical to Snapcaster Mage's
/// (<see cref="SnapcasterMageFactory"/>): an ETB triggered ability declaring a
/// 1..1 <see cref="TargetRequest"/> for an instant or sorcery card in the
/// controller's graveyard, granting runtime flashback at the chosen card's own
/// printed mana cost (CR 702.34 — "the flashback cost is equal to its mana
/// cost") via <see cref="Card.GrantRuntimeFlashback"/>. The grant is cleared on
/// the next Cleanup step (CR 514.2) when an <see cref="IEventBus"/> is wired.
///
/// To cast the flashback-granted card, callers build a
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> from the card's
/// <see cref="Card.RuntimeFlashbackCost"/> and pass it to
/// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>; that path exiles the
/// card on resolution (CR 702.34b). Bots discover the granted flashback through
/// <see cref="Majik.Core.Players.Agents.RuntimeFlashbackAltCostProbe"/>. No new
/// spell-cast plumbing is introduced — this reuses the exact Snapcaster path.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — canonical / shape build. The ETB grant is
///   stamped on resolution but the EOT cleanup hook is inert (no bus).
/// - <see cref="Create(Player, IEventBus?)"/> — wires the CR 514.2 end-of-turn
///   clear. This is the effects-aware overload the production routed build
///   reaches via <see cref="NamedCardFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Plot (CR 718)</b>: the printed "Plot {2}{U}" rider is NOT wired, the
///   same deferral <see cref="SlickshotShowOffFactory"/> documents. Plot is the
///   cast-from-exile-on-a-later-turn-at-sorcery-speed alt-cost primitive cluster
///   (pay {2}{U} from hand to exile with a plotted marker; on a later turn cast
///   from exile for {0} at sorcery speed, once per turn — CR 718.2). No
///   activated-from-hand-with-alt-cost + sorcery-speed-later-turn permission
///   primitive exists yet; deferred until that primitive lands. The card ships
///   as a 2/3 body with the Snapcaster-style ETB until Plot is wired.
/// </summary>
[CardName("Slickshot Lockpicker")]
public static class SlickshotLockpickerFactory
{
    public const string CardName = "Slickshot Lockpicker";
    public const string Slug = "slickshot-lockpicker";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Canonical build (shape / dispatcher path). The ETB grant resolves but
    /// the EOT-cleanup hook is inert without a bus.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the source generator recognises and the
    /// production <c>GameFacade</c> routed build dispatches to. The bus drives
    /// the CR 514.2 end-of-turn clear of the runtime flashback grant. Forwards
    /// the bus off <paramref name="effects"/>.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Construct Slickshot Lockpicker, attaching the Snapcaster-style ETB
    /// flashback-grant trigger. When <paramref name="eventBus"/> is supplied the
    /// grant is cleared on the next Cleanup step (CR 514.2); when null the grant
    /// persists (shape / direct-call path).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 603.6a — ETB triggered ability. Declares a mandatory 1..1 target
        // request for an instant or sorcery card in the controller's graveyard;
        // on resolution grants flashback (CR 702.34) until end of turn with cost
        // equal to the chosen card's printed mana cost.
        TriggeredAbility? etb = null;
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName} — target instant or sorcery in your graveyard gains flashback until end of turn (cost = its mana cost)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Card target) return;

                // CR 603.10b — illegal-on-resolution check: still in the
                // controller's graveyard and still an instant or sorcery card.
                if (target.Zone != ZoneType.Graveyard) return;
                if (!ReferenceEquals(target.Owner, owner)) return;
                if (!target.HasType(CardType.Instant) && !target.HasType(CardType.Sorcery)) return;

                // Stamp the grant — cost = the target's printed mana cost.
                target.GrantRuntimeFlashback(target.ManaCostValue);

                // CR 514.2 — clear the grant on the next Cleanup step. No bus ⇒
                // the grant persists (caller manages EOT manually).
                if (eventBus == null) return;

                Action<StepStartedEvent>? handler = null;
                handler = e =>
                {
                    if (e.StepType != StepStateType.Cleanup) return;
                    target.ClearRuntimeFlashback();
                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etb);

        return card;
    }
}
