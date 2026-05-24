using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dragon's Rage Channeler (Modern Horizons 2, {R}).
///
/// Creature — Human Shaman 1/1. Oracle text:
///   "Whenever you cast a noncreature spell, surveil 1.
///    Delirium — Dragon's Rage Channeler gets +2/+2 and has flying as long
///    as there are four or more card types among cards in your graveyard."
///
/// ## Implementation
///
/// - 1/1 Human Shaman with mana cost {R}.
///
/// - <b>Noncreature-cast surveil trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> matches
///   when the spell's controller is Dragon's Rage Channeler's controller AND
///   the spell's card does NOT have <see cref="CardType.Creature"/>. Effect:
///   surveil 1 (CR 701.42) — peek the top card, route the decision through
///   the controller's <see cref="IPlayerAgent.ChooseSurveilDecisionAsync"/>
///   when registered, fall back to all-to-graveyard otherwise. Same agent /
///   fall-back pattern as <see cref="LedgerShredderFactory"/> and
///   <see cref="LibrarySurveyorFactory"/>. DRC's own cast (a creature spell)
///   does NOT trigger this — the noncreature predicate filters it out.
///
/// - <b>Delirium conditional static (CR 702.105 / 613.1f)</b>: a
///   <see cref="DeliriumPumpEffect"/> registered with the
///   <see cref="ContinuousEffectsService"/> when the runtime overload is
///   used. The effect is active iff DRC is on the battlefield AND the
///   controller's graveyard has 4+ distinct
///   <see cref="CardType"/> values (sampled via
///   <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> on every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/>, so changes
///   to the graveyard reflect immediately — no event subscriptions). When
///   active, the effect adds +2/+2 in Layer 7c and grants the "Flying"
///   keyword in Layer 6 (one registered effect per layer — see
///   <see cref="DeliriumPumpEffect"/>).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The surveil trigger
///   is attached for ability-shape observability but no TriggerManager is
///   registered; the delirium static is not wired to a
///   <see cref="ContinuousEffectsService"/>. Suitable for dispatcher /
///   structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully-wired. When a trigger manager is supplied, the surveil
///   trigger fires from the bus. When a continuous-effects service is
///   supplied, the +2/+2 pump and Flying grant register / unregister via
///   a battlefield-zone lifecycle handler subscribed to the bus
///   (mirrors <see cref="TarmogoyfFactory"/>'s lifecycle wiring).
/// </summary>
[CardName("Dragon's Rage Channeler")]
public static class DragonsRageChannelerFactory
{
    public const string CardName = "Dragon's Rage Channeler";
    public const string Cost = "{R}";
    public const int DeliriumThreshold = 4;

    /// <summary>
    /// Construct Dragon's Rage Channeler with no live wiring. The
    /// surveil trigger is attached to the card for shape observability;
    /// the delirium static is not registered with a continuous-effects
    /// service. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Dragon's Rage Channeler with optional runtime services.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trigger 1 — "Whenever you cast a noncreature spell, surveil 1."
        // CR 603.1 + CR 701.42. Predicate: spell controller is DRC's
        // controller AND the spell's card is not a Creature. (Other types
        // like Artifact/Sorcery/Instant — and any future Battle-spell —
        // still qualify; "noncreature" only excludes the Creature type.)
        // ----------------------------------------------------------------
        var surveilCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && !e.Spell.Card.HasType(CardType.Creature));

        var surveilEffect = new Effect(
            "Dragon's Rage Channeler — surveil 1 (whenever you cast a noncreature spell)",
            () =>
            {
                var peeked = Majik.Core.Keywords.SurveilAction.Peek(owner, 1);
                if (peeked.Count == 0) return;

                var agent = AgentRegistry.Get(owner);
                Majik.Core.Keywords.SurveilAction.SurveilDecision decision;
                if (agent != null)
                {
                    decision = agent.ChooseSurveilDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    // Default: all peeked cards go to graveyard (matches
                    // Ledger Shredder / Library Surveyor / Underground
                    // Mortuary v1 behavior when no agent is registered).
                    decision = new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                Majik.Core.Keywords.SurveilAction.Apply(owner, 1, decision);
            });

        var surveilTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: surveilCondition,
            effects: new IEffect[] { surveilEffect });

        card.AddAbility(surveilTrigger);
        triggers?.RegisterTriggeredAbility(surveilTrigger);

        // ----------------------------------------------------------------
        // Delirium static — "DRC gets +2/+2 and has flying as long as
        // there are four or more card types among cards in your
        // graveyard." (CR 702.105 / CR 613.1f). Two continuous effects
        // register together — one Layer 7c (+2/+2) and one Layer 6
        // (Flying grant). Both gate IsActive() on DRC being on the
        // battlefield AND delirium being satisfied (sampled live from
        // controller's graveyard on every Compute).
        //
        // When no ContinuousEffectsService is supplied (shape-only
        // path), the effects aren't registered — card shape still
        // reflects the printed 1/1 with the trigger attached.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new DeliriumLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Sample the controller's graveyard for delirium (CR 702.105):
    /// true iff there are 4+ distinct <see cref="CardType"/> values
    /// across cards in <paramref name="controller"/>'s graveyard.
    /// Reuses <see cref="TarmogoyfFactory.CountDistinctCardTypes"/>.
    /// </summary>
    public static bool IsDeliriumActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return TarmogoyfFactory.CountDistinctCardTypes(
            controller.Zones.Graveyard.GetCards()) >= DeliriumThreshold;
    }

    /// <summary>
    /// CR 613.1f — continuous effect that pumps DRC's P/T by +2/+2
    /// (Layer 7c) OR grants the Flying keyword (Layer 6), gated on
    /// delirium (CR 702.105). One instance per layer is registered by
    /// <see cref="DeliriumLifecycle"/>.
    /// </summary>
    private sealed class DeliriumPumpEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly Layer _layer;

        public DeliriumPumpEffect(Creature source, Player controller, Layer layer)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _layer = layer;
        }

        public override Layer Layer => _layer;

        public override Permanent? Source => _source;

        public override bool IsActive() =>
            _source.Zone == Majik.Core.Zones.ZoneType.Battlefield
            && IsDeliriumActive(_controller);

        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _source);

        public override void Apply(CreatureCharacteristics chars)
        {
            if (_layer == Layer.PT_Modify)
            {
                chars.Power += 2;
                chars.Toughness += 2;
            }
            else if (_layer == Layer.Abilities)
            {
                chars.Keywords.Add("Flying");
            }
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for DRC's delirium static. Registers
    /// the +2/+2 (Layer 7c) and Flying (Layer 6) effects when DRC enters
    /// the battlefield; unregisters when it leaves. Mirrors
    /// <see cref="TarmogoyfFactory"/>'s lifecycle shape.
    /// </summary>
    private sealed class DeliriumLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private DeliriumPumpEffect? _pumpRegistered;
        private DeliriumPumpEffect? _flyingRegistered;
        private bool _attached;

        public DeliriumLifecycle(
            Creature source,
            Player controller,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _controller = controller;
            _effects = effects;
            _eventBus = eventBus;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _source.ActiveEffects = _effects;
            _eventBus?.SubscribeAll(_handler);
            Sync();
        }

        private void OnEvent(GameEvent e)
        {
            if (e is not CardMovedEvent moved) return;
            if (!ReferenceEquals(moved.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;
            if (shouldBeActive && _pumpRegistered == null)
            {
                _pumpRegistered = new DeliriumPumpEffect(_source, _controller, Layer.PT_Modify);
                _flyingRegistered = new DeliriumPumpEffect(_source, _controller, Layer.Abilities);
                _effects.Register(_pumpRegistered);
                _effects.Register(_flyingRegistered);
            }
            else if (!shouldBeActive && _pumpRegistered != null)
            {
                _effects.Unregister(_pumpRegistered);
                if (_flyingRegistered != null) _effects.Unregister(_flyingRegistered);
                _pumpRegistered = null;
                _flyingRegistered = null;
            }
        }
    }
}
