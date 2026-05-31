using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Knight of the Reliquary (Conflux, {1}{G}{W}).
///
/// Creature — Human Knight 2/2. Oracle text:
///   "Knight of the Reliquary gets +1/+1 for each land card in your graveyard.
///    {T}, Sacrifice a Forest or Plains: Search your library for a land card,
///    put that card onto the battlefield, then shuffle."
///
/// ## Implementation
///
/// ### Part 1 — Self-pump (Layer 7c)
///
/// "Knight of the Reliquary gets +1/+1 for each land card in your graveyard."
/// This is a <b>Layer 7c static modification</b> (CR 613.1g) — not a CDA
/// (Layer 7a), because Knight has a printed base P/T (2/2) that serves as
/// the foundation; the pump stacks on top as a modifier. Mirrors
/// <see cref="TerritorialKavuFactory"/>'s Domain-pump shape, swapping the
/// "distinct basic land types you control" count for "land cards in your
/// graveyard".
///
/// Implemented via <see cref="LandsInGraveyardPumpEffect"/>, a
/// <see cref="ContinuousEffect"/> subclass that re-counts the controller's
/// graveyard land cards on every <see cref="ContinuousEffectsService.Compute"/>
/// invocation. Lifecycle: ETB registers the effect; LTB unregisters it
/// (mirrors <see cref="TarmogoyfFactory"/> + <see cref="TerritorialKavuFactory"/>
/// via <see cref="CardMovedEvent"/> on the supplied <see cref="IEventBus"/>).
///
/// ### Part 2 — Tutor activated ability
///
/// "{T}, Sacrifice a Forest or Plains: Search your library for a land card,
/// put that card onto the battlefield, then shuffle." Single
/// <see cref="ActivatedAbility"/> with three costs:
///   - <see cref="AdditionalCost.Tap"/> on Knight,
///   - <see cref="AdditionalCost.Sacrifice"/> on a Forest or Plains the
///     controller controls (v1 deterministic — first eligible permanent;
///     the sacrifice payment is performed by the effect closure as a
///     stand-in until <see cref="AdditionalCost.Pay"/> sacrifices reach
///     the engine, matching Expedition Map's sac-self posture).
///
/// On resolution: route through the controller's agent via
/// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for the land pick
/// (deterministic first-land fallback when no agent is registered — same
/// posture as Expedition Map / Stoneforge Mystic / Sylvan Scrying). Move
/// Library → Battlefield via <see cref="ZoneService.MoveCard"/> when
/// supplied (so ETB triggers + replacements on the played land fire —
/// CR 603.6a / CR 614, PR #537); fall back to raw zone manipulation
/// otherwise. Shuffle via <see cref="LibraryShuffle.ShuffleLibrary"/>
/// (CR 701.20a — publishes <c>LibraryShuffledEvent</c> when a bus is
/// registered).
///
/// Tutors ANY land — basic or nonbasic — distinct from the
/// "Forest or Plains" cost-side type gate. The tutored land enters
/// untapped (printed text doesn't say "tapped"), distinct from Primeval
/// Titan's tapped-entry rider.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-cost agent prompt</b>: the v1 picker takes the first
///   Forest / Plains on the controller's battlefield. A real implementation
///   would consult the agent — same gap as every other "sacrifice a [type]"
///   additional-cost factory (Bone Splinters, Caustic Caterpillar). The
///   sacrifice payment runs inside the effect closure rather than through
///   <see cref="AdditionalCost.Pay"/> since the latter is a no-op stub.
/// - <b>Land-pick agent prompt</b>: deterministic first-land fallback when
///   no agent is registered. Production callers register an agent via
///   <see cref="AgentRegistry"/> so the pick is bot-driven.
/// - <b>Reveal-event emission</b>: tutoring the land onto the battlefield
///   doesn't publish a reveal event; same gap as every tutor factory.
/// </summary>
[CardName("Knight of the Reliquary")]
public static class KnightOfTheReliquaryFactory
{
    public const string CardName = "Knight of the Reliquary";
    public const string PrintedManaCost = "{1}{G}{W}";

    /// <summary>
    /// Construct Knight of the Reliquary with no live continuous-effects /
    /// trigger / zone-service wiring (the shape/dispatcher path). The pump
    /// is attached to the card structurally but not registered; the
    /// activated ability falls back to raw zone manipulation. Suitable for
    /// factory-shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, zoneService: null);

    /// <summary>
    /// Construct Knight of the Reliquary with optional runtime services.
    /// <para>When <paramref name="effects"/> is supplied, a
    /// <see cref="LandsInGraveyardPumpEffect"/> is registered so the
    /// self-pump is evaluated on every <see cref="ContinuousEffectsService.Compute"/>
    /// call. When <paramref name="eventBus"/> is also supplied, the
    /// lifecycle binder subscribes to <see cref="CardMovedEvent"/> so the
    /// effect registers on ETB and unregisters on LTB (mirrors
    /// <see cref="TerritorialKavuFactory"/> / <see cref="TarmogoyfFactory"/>).</para>
    /// <para>When <paramref name="zoneService"/> is supplied, the tutor
    /// activated ability routes the Library → Battlefield move through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers + replacement
    /// effects on the tutored land fire (CR 603.6a / CR 614, PR #537).</para>
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Part 1 — Self-pump. Layer 7c static effect.
        //   "Knight of the Reliquary gets +1/+1 for each land card in your
        //    graveyard." CR 613.1g.
        // Re-counts the controller's graveyard lands on every Compute call.
        // Lifecycle: register on ETB, unregister on LTB.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new ReliquaryPumpLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // Part 2 — Tutor activated ability.
        //   "{T}, Sacrifice a Forest or Plains: Search your library for a
        //    land card, put that card onto the battlefield, then shuffle."
        // CR 602 — activated ability. CR 701.19a — search consults agent
        // (null = decline; legal). CR 701.20a — shuffle after.
        // CR 305.9 / 113.6c — putting a land directly onto the battlefield
        // is NOT a land drop; bypasses the per-turn cap.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: sac Forest/Plains, tutor a land -> battlefield, shuffle",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 117 — sacrifice cost payment (effect-closure stand-in
                // until AdditionalCost.Sacrifice runs the move itself).
                // v1 picker: first Forest or Plains on the controller's
                // battlefield. Knight may sacrifice ITSELF if it happens
                // to be retyped as a Forest / Plains (Sylvan Awakening,
                // not in scope here — but the first-match scan is
                // type-safe regardless).
                if (!PaySacrificeCost(controller)) return;

                // CR 701.19a — search for a land card in the library.
                var candidates = controller.Zones.Library.GetCards()
                    .Where(c => c.HasType(CardType.Land))
                    .ToList();

                if (candidates.Count > 0)
                {
                    var agent = AgentRegistry.Get(controller);
                    ICard? pick = agent != null
                        ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "land card")
                            .GetAwaiter().GetResult()
                        : candidates[0];

                    if (pick != null)
                    {
                        // Library → Battlefield. Prefer ZoneService so
                        // ETB triggers + replacements on the tutored land
                        // fire (CR 603.6a / CR 614, PR #537). Fall back
                        // to a registry lookup when no service was passed
                        // explicitly; raw zone manipulation as last resort.
                        var zones = zoneService
                            ?? Majik.Core.Services.ZoneServiceRegistry.Get(controller);
                        if (zones != null)
                        {
                            zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, controller);
                        }
                        else
                        {
                            controller.Zones.Library.RemoveCard(pick);
                            controller.Zones.Battlefield.AddCard(pick);
                            pick.SetZone(ZoneType.Battlefield);
                            pick.SetController(controller);
                        }
                    }
                }

                // CR 701.20a — shuffle after the search resolves
                // (regardless of whether a land was picked).
                LibraryShuffle.ShuffleLibrary(controller, "knight-of-the-reliquary");
            });

        var tutorAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(tutorAbility);

        return card;
    }

    /// <summary>
    /// Pay the printed sacrifice cost: sacrifice a Forest or Plains the
    /// controller controls. v1 deterministic — first matching permanent
    /// on the battlefield. Returns false when no eligible permanent
    /// exists (the activation should already have been gated by the
    /// engine's cost-payability check, but the closure double-guards).
    /// </summary>
    private static bool PaySacrificeCost(Player controller)
    {
        var pick = controller.Zones.Battlefield.GetCards()
            .OfType<Land>()
            .FirstOrDefault(l => ReferenceEquals(l.Controller, controller)
                && (l.HasSubtype(CardSubtype.Forest)
                    || l.HasSubtype(CardSubtype.Plains)));
        if (pick == null) return false;

        // CR 701.16 — sacrifice = move battlefield → owner's graveyard.
        var sacOwner = pick.Owner ?? controller;
        controller.Zones.Battlefield.RemoveCard(pick);
        sacOwner.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        return true;
    }

    /// <summary>
    /// Count the number of cards in <paramref name="controller"/>'s
    /// graveyard with the Land type. Pure helper exposed for tests and
    /// for the live <see cref="LandsInGraveyardPumpEffect"/> evaluator.
    /// </summary>
    public static int CountLandsInGraveyard(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        int n = 0;
        foreach (var c in controller.Zones.Graveyard.GetCards())
        {
            if (c.HasType(CardType.Land)) n++;
        }
        return n;
    }

    // -----------------------------------------------------------------------
    // LandsInGraveyardPumpEffect — Layer 7c live-count self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Knight of the Reliquary's self-pump.
    /// On every <see cref="ContinuousEffectsService.Compute"/> invocation
    /// this counts the controller's graveyard lands and applies +N/+N to
    /// Knight's characteristics.
    ///
    /// <para>Active only while Knight is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the
    /// ETB/LTB lifecycle wiring in <see cref="ReliquaryPumpLifecycle"/>).</para>
    /// </summary>
    public sealed class LandsInGraveyardPumpEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public LandsInGraveyardPumpEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Knight is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Knight itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +N/+N where N = land cards in the controller's graveyard.
        /// Reads <see cref="Permanent.Controller"/> live so a control-change
        /// retargets the pump to the new controller's graveyard (CR 109.5 /
        /// 613 — "your" reads at evaluation time).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller ?? _source.Owner;
            if (controller == null) return;
            var n = CountLandsInGraveyard(controller);
            chars.Power += n;
            chars.Toughness += n;
        }
    }

    // -----------------------------------------------------------------------
    // ReliquaryPumpLifecycle — ETB/LTB wiring for the pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Knight's self-pump. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers
    /// <see cref="LandsInGraveyardPumpEffect"/> when Knight enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="TarmogoyfFactory"/>'s <c>TarmogoyfCdaLifecycle</c> and
    /// <see cref="TerritorialKavuFactory"/>'s <c>DomainPumpLifecycle</c>.
    /// </summary>
    private sealed class ReliquaryPumpLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private LandsInGraveyardPumpEffect? _registered;
        private bool _attached;

        public ReliquaryPumpLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
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
            var moved = e;
            if (!ReferenceEquals(moved.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new LandsInGraveyardPumpEffect(_source);
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
