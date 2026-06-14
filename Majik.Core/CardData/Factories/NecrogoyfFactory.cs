using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Necrogoyf (Modern Horizons 2, {3}{B}{B}).
///
/// Creature — Lhurgoyf */4. Oracle text (verified against Scryfall 2026-06-14):
///   "Necrogoyf's power is equal to the number of creature cards in all
///    graveyards.
///    At the beginning of each player's upkeep, that player discards a card.
///    Madness {1}{B}{B}"
///
/// ## Shape source
/// Card identity (name, {3}{B}{B}, 0/4 placeholder, Creature — Lhurgoyf) is
/// loaded from <c>Majik.Core/CardData/Cards/necrogoyf.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The CDA power + upkeep discard trigger
/// are attached in code below.
///
/// ## Implemented (v1)
///
/// - <b>*/4 Creature — Lhurgoyf at {3}{B}{B}.</b> Toughness is a FIXED printed
///   4; only the power is characteristic-defined.
///
/// - <b>"Necrogoyf's power is equal to the number of creature cards in all
///   graveyards" (CR 604.3 / CR 613.2 — Layer 7a CDA).</b> Implemented via a
///   <see cref="CdaPowerToughnessEffect"/> whose <c>powerOf</c> counts creature
///   cards across every graveyard in the game and whose <c>toughnessOf</c>
///   returns the constant 4 (the CDA SETS both in Layer 7a; the toughness
///   value is just the printed 4). Power-counting shape is shared with
///   <see cref="MortivoreFactory.CountCreatureCards"/> — the difference from
///   Mortivore is purely the fixed toughness. Printed P/T = 0/4 placeholder
///   (CR 208.2c — Layer 7a overwrites the power on every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/>).
///
/// - <b>"At the beginning of each player's upkeep, that player discards a
///   card" (CR 603.1 / CR 500.4).</b> Symmetric upkeep trigger (fires on EVERY
///   player's upkeep, like Sulfuric Vortex / Asylum Visitor). The active
///   upkeep player is captured off <see cref="StepStartedEvent.Player"/>; on
///   resolution THAT player discards a card via <see cref="Fx.Discard"/> (the
///   central discard funnel — v1 deterministic first-card pick, agent choice
///   deferred, same posture as Liliana of the Veil).
///
/// ## Madness (NOT wired here — intrinsic)
/// Madness {1}{B}{B} works intrinsically for every catalogued card (CR 702.35)
/// via <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the
/// central discard funnel <see cref="Fx.DiscardCard"/>; "Necrogoyf" is
/// catalogued at {1}{B}{B}, so the madness line needs no factory code.
///
/// ## Lifecycle
///
/// Mirrors <see cref="MortivoreFactory"/> — the single-argument
/// <see cref="Create(Player)"/> overload produces a shape-correct card with
/// the upkeep trigger attached (auto-registered by the engine's
/// <see cref="TriggerManager"/> on battlefield entry) but no live CDA. The
/// wiring overload
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?, Func{IEnumerable{ICard}})"/>
/// attaches the live Layer 7a CDA + registers the upkeep trigger.
/// </summary>
[CardName("Necrogoyf")]
public static class NecrogoyfFactory
{
    public const string CardName = "Necrogoyf";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("necrogoyf");

    /// <summary>Fixed printed toughness (CR 208.2c — only power is CDA).</summary>
    public const int FixedToughness = 4;

    /// <summary>
    /// Creates a Necrogoyf with correct card identity + the upkeep discard
    /// trigger attached (auto-registered on battlefield entry), but no live
    /// Layer 7a CDA. Suitable for factory-shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null, graveyardSource: null);

    /// <summary>
    /// Creates a fully-wired Necrogoyf. When <paramref name="effects"/> and
    /// <paramref name="graveyardSource"/> are supplied, a
    /// <see cref="CdaPowerToughnessEffect"/> is attached so the Layer 7a CDA
    /// power registers/unregisters as Necrogoyf enters/leaves the battlefield
    /// via <see cref="CardMovedEvent"/> on <paramref name="eventBus"/>. The
    /// each-player's-upkeep discard trigger is always attached.
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the CDA
    /// against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be null.</param>
    /// <param name="graveyardSource">Closure returning every card in every
    /// graveyard in the game. Read fresh on every Compute. Pass null for
    /// shape-only.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? graveyardSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "At the beginning of each player's upkeep, that player discards a
        //  card." — CR 603.1 / CR 500.4.
        // Symmetric — fires on EVERY player's upkeep (StepStartedEvent.Player
        // captures the active upkeep player). On resolution THAT player
        // discards a card through the central discard funnel.
        // ----------------------------------------------------------------
        Player? upkeepPlayer = null;

        var upkeepCondition = new EventTriggerCondition<StepStartedEvent>((e, _) =>
        {
            if (e.StepType != StepStateType.Upkeep) return false;
            upkeepPlayer = e.Player;
            return true;
        });

        var upkeepEffect = new Effect(
            $"{CardName}: that player discards a card (each player's upkeep)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var target = upkeepPlayer;
                upkeepPlayer = null;
                if (target == null) return;
                // CR 701.16 — "that player discards a card." Routes through
                // Fx.Discard (the central funnel) so the discard publishes a
                // DiscardedEvent and any madness/discard triggers observe it.
                Fx.Discard(target, 1);
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: upkeepCondition,
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // Layer 7a CDA power lifecycle wiring (toughness is the fixed 4).
        // ----------------------------------------------------------------
        if (effects != null && graveyardSource != null)
        {
            var lifecycle = new NecrogoyfCdaLifecycle(
                card,
                effects,
                eventBus,
                graveyardSource);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Necrogoyf's CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> (power = creature-card count
    /// across all graveyards, toughness = the constant 4) when Necrogoyf
    /// enters the battlefield, unregisters when it leaves. Mirrors Mortivore's
    /// lifecycle — only the toughness evaluator differs (constant vs. count).
    /// </summary>
    private sealed class NecrogoyfCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _graveyardSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public NecrogoyfCdaLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<IEnumerable<ICard>> graveyardSource)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _graveyardSource = graveyardSource;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus?.Subscribe(_handler);
            Sync();
        }

        private void OnEvent(CardMovedEvent e)
        {
            if (!ReferenceEquals(e.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    powerOf: _ => MortivoreFactory.CountCreatureCards(_graveyardSource()),
                    toughnessOf: _ => FixedToughness);
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
