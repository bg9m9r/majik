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
/// Named-card factory for Elvish Reclaimer (Modern Horizons, {G}).
///
/// Creature — Elf Warrior 1/2. Oracle (verified against Scryfall 2026-06-14):
///   "This creature gets +2/+2 as long as there are three or more land cards
///    in your graveyard.
///    {2}, {T}, Sacrifice a land: Search your library for a land card, put it
///    onto the battlefield tapped, then shuffle."
///
/// Near-identical in shape to <see cref="KnightOfTheReliquaryFactory"/> — a
/// graveyard-land-count self-pump plus a tap+sac-a-land tutor — with four
/// differences:
///   1. The pump is a <b>conditional +2/+2</b> (active iff &gt;= 3 land cards in
///      the controller's graveyard), not Knight's +1/+1-per-land. CR 613.1g.
///   2. The sacrifice cost is <b>any land</b> (not "Forest or Plains").
///   3. The fetch carries an extra generic <b>{2}</b> on top of {T}, Sacrifice.
///      CR 117.5.
///   4. The tutored land enters <b>tapped</b> (Knight's enters untapped).
///
/// ## Part 1 — Conditional self-pump (Layer 7c)
/// "...gets +2/+2 as long as there are three or more land cards in your
/// graveyard." A <b>Layer 7c static modification</b> (CR 613.1g) — Elvish
/// Reclaimer has a printed base P/T (1/2), and the pump stacks on top as a
/// modifier when the threshold is met. Implemented via
/// <see cref="ThreeLandThresholdPumpEffect"/>, a <see cref="ContinuousEffect"/>
/// that re-evaluates the controller's graveyard land count on every
/// <see cref="ContinuousEffectsService.Compute"/> invocation and applies +2/+2
/// only while the count is &gt;= 3. ETB registers the effect; LTB unregisters it
/// (mirrors <see cref="KnightOfTheReliquaryFactory"/> via
/// <see cref="CardMovedEvent"/> on the supplied <see cref="IEventBus"/>).
///
/// ## Part 2 — Tutor activated ability
/// "{2}, {T}, Sacrifice a land: Search your library for a land card, put it
/// onto the battlefield tapped, then shuffle." Single
/// <see cref="ActivatedAbility"/> with the printed {2} (<see cref="ManaCostCost"/>)
/// plus <see cref="AdditionalCost.Tap"/>; the sacrifice-a-land cost is paid by
/// the effect closure (v1 deterministic — first eligible land, matching every
/// other "sacrifice a [type]" factory; see Deferred). Tutors ANY land — basic
/// or nonbasic — onto the battlefield tapped (CR 305.9 / 113.6c — putting a
/// land directly onto the battlefield is NOT a land drop), routing the
/// Library → Battlefield move through <see cref="ZoneService"/> when supplied
/// so ETB triggers + replacements on the played land fire (CR 603.6a / CR 614).
/// Shuffle via <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a).
///
/// ## Deferred (v1 gaps — shared with every tutor factory)
/// - <b>Sacrifice-cost agent prompt</b>: the v1 picker sacrifices the first
///   land on the controller's battlefield (it will avoid sacrificing Elvish
///   Reclaimer itself, which is not a land). A real implementation consults the
///   agent — same gap as Knight of the Reliquary / Bone Splinters.
/// - <b>Land-pick agent prompt</b>: deterministic first-land fallback when no
///   agent is registered.
/// - <b>Reveal-event emission</b>: tutoring the land onto the battlefield does
///   not publish a reveal event; same gap as every tutor factory.
/// </summary>
[CardName("Elvish Reclaimer")]
public static class ElvishReclaimerFactory
{
    public const string CardName = "Elvish Reclaimer";
    public const string PrintedManaCost = "{G}";

    /// <summary>
    /// Number of land cards that must be in the controller's graveyard for the
    /// +2/+2 pump to be active (CR 613.1g — "three or more").
    /// </summary>
    public const int PumpThreshold = 3;

    /// <summary>
    /// Construct Elvish Reclaimer with no live continuous-effects / trigger /
    /// zone-service wiring (the shape/dispatcher path). Suitable for
    /// factory-shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, zoneService: null);

    /// <summary>
    /// Construct Elvish Reclaimer with optional runtime services.
    /// <para>When <paramref name="effects"/> is supplied, a
    /// <see cref="ThreeLandThresholdPumpEffect"/> is registered so the
    /// conditional +2/+2 is evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> call. When
    /// <paramref name="eventBus"/> is also supplied, the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effect registers on
    /// ETB and unregisters on LTB.</para>
    /// <para>When <paramref name="zoneService"/> is supplied, the tutor
    /// activated ability routes the Library → Battlefield move through
    /// <see cref="ZoneService"/> so ETB triggers + replacement effects on the
    /// tutored land fire (CR 603.6a / CR 614).</para>
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
            power: 1,
            toughness: 2,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Part 1 — Conditional self-pump. Layer 7c static effect.
        //   "...gets +2/+2 as long as there are three or more land cards in
        //    your graveyard." CR 613.1g.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new ReclaimerPumpLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // Part 2 — Tutor activated ability.
        //   "{2}, {T}, Sacrifice a land: Search your library for a land card,
        //    put it onto the battlefield tapped, then shuffle."
        // CR 602 — activated ability. CR 117.5 — printed {2} extra. CR 701.19a
        // — search consults agent (null = decline; legal). CR 701.20a — shuffle
        // after. CR 305.9 / 113.6c — putting a land directly onto the
        // battlefield is NOT a land drop; bypasses the per-turn cap.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: sac a land, tutor a land -> battlefield tapped, shuffle",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 117 — sacrifice cost payment (effect-closure stand-in
                // until AdditionalCost.Sacrifice runs the move itself). v1
                // picker: first land on the controller's battlefield.
                if (!PaySacrificeCost(controller)) return;

                // CR 701.19a — search for a land card in the library.
                var candidates = controller.Zones.Library.GetCards()
                    .Where(c => c.HasType(CardType.Land))
                    .ToList();

                if (candidates.Count > 0)
                {
                    var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                    ICard? pick = agent != null
                        ? (await agent.ChooseLibraryPickAsync(ctx: ctx.Game, candidates, "land card").ConfigureAwait(false))
                        : candidates[0];

                    if (pick != null)
                    {
                        // Library → Battlefield. Prefer ZoneService so ETB
                        // triggers + replacements on the tutored land fire
                        // (CR 603.6a / CR 614). Fall back to a registry lookup
                        // when no service was passed; raw zone manipulation as
                        // last resort.
                        var zones = zoneService
                            ?? Majik.Core.Services.ZoneServiceRegistry.Get(controller);
                        if (zones != null)
                        {
                            await zones.MoveCardToAsync(
                                pick, ZoneType.Battlefield, ctx, controller: controller)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            controller.Zones.Library.RemoveCard(pick);
                            controller.Zones.Battlefield.AddCard(pick);
                            pick.SetZone(ZoneType.Battlefield);
                            pick.SetController(controller);
                        }

                        // "...put it onto the battlefield tapped." Apply the
                        // tapped rider after the move (CR 614 — the land has
                        // entered; tap it now). Distinct from Knight of the
                        // Reliquary, whose tutored land enters untapped.
                        if (pick is Permanent perm && !perm.IsTapped)
                        {
                            perm.Tap();
                        }
                    }
                }

                // CR 701.20a — shuffle after the search resolves (regardless
                // of whether a land was picked).
                LibraryShuffle.ShuffleLibrary(controller, "elvish-reclaimer");
            });

        var tutorAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(tutorAbility);

        return card;
    }

    /// <summary>
    /// Pay the printed sacrifice cost: sacrifice a land the controller
    /// controls. v1 deterministic — first land on the battlefield. Returns
    /// false when no land exists (the activation should already have been
    /// gated by the engine's cost-payability check, but the closure
    /// double-guards). Elvish Reclaimer itself is a creature, never a land, so
    /// the scan never self-sacrifices.
    /// </summary>
    private static bool PaySacrificeCost(Player controller)
    {
        var pick = controller.Zones.Battlefield.GetCards()
            .OfType<Land>()
            .FirstOrDefault(l => ReferenceEquals(l.Controller, controller));
        if (pick == null) return false;

        // CR 701.16 — sacrifice = move battlefield → owner's graveyard.
        var sacOwner = pick.Owner ?? controller;
        controller.Zones.Battlefield.RemoveCard(pick);
        sacOwner.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        return true;
    }

    /// <summary>
    /// Count the number of cards in <paramref name="controller"/>'s graveyard
    /// with the Land type. Pure helper exposed for tests and for the live
    /// <see cref="ThreeLandThresholdPumpEffect"/> evaluator.
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
    // ThreeLandThresholdPumpEffect — Layer 7c conditional self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Elvish Reclaimer's conditional self-pump.
    /// On every <see cref="ContinuousEffectsService.Compute"/> invocation this
    /// applies +2/+2 to Elvish Reclaimer iff the controller's graveyard holds
    /// &gt;= <see cref="PumpThreshold"/> land cards (CR 613.1g).
    ///
    /// <para>Active only while Elvish Reclaimer is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the ETB/LTB
    /// lifecycle wiring in <see cref="ReclaimerPumpLifecycle"/>).</para>
    /// </summary>
    public sealed class ThreeLandThresholdPumpEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public ThreeLandThresholdPumpEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Elvish Reclaimer is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Elvish Reclaimer itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +2/+2 iff the controller's graveyard holds &gt;= 3 land cards.
        /// Reads <see cref="Permanent.Controller"/> live so a control-change
        /// retargets the condition to the new controller's graveyard
        /// (CR 109.5 / 613 — "your" reads at evaluation time).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller ?? _source.Owner;
            if (controller == null) return;
            if (CountLandsInGraveyard(controller) >= PumpThreshold)
            {
                chars.Power += 2;
                chars.Toughness += 2;
            }
        }

        /// <summary>
        /// Sim-only: reconstruct an identical
        /// <see cref="ThreeLandThresholdPumpEffect"/> bound to
        /// <paramref name="clonedSource"/> for the search-sandbox clone. The
        /// graveyard count reads clonedSource.Controller live (correctly
        /// remapped).
        /// preserves: nothing scalar; source → clonedSource (as Creature).
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Majik.Core.Cards.Permanent clonedSource,
            System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
            => clonedSource is Majik.Core.Cards.Creature clonedCreature
                ? new ThreeLandThresholdPumpEffect(clonedCreature)
                : null;
    }

    // -----------------------------------------------------------------------
    // ReclaimerPumpLifecycle — ETB/LTB wiring for the pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Elvish Reclaimer's conditional self-pump.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers
    /// <see cref="ThreeLandThresholdPumpEffect"/> when Elvish Reclaimer enters
    /// the battlefield, unregisters when it leaves. Mirrors
    /// <see cref="KnightOfTheReliquaryFactory"/>'s lifecycle binder.
    /// </summary>
    private sealed class ReclaimerPumpLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private ThreeLandThresholdPumpEffect? _registered;
        private bool _attached;

        public ReclaimerPumpLifecycle(
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
            if (!ReferenceEquals(e.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new ThreeLandThresholdPumpEffect(_source);
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
