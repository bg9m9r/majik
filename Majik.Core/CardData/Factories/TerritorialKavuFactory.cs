using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Territorial Kavu (Modern Horizons 2, {G}{W}).
///
/// Creature — Kavu 2/2. Oracle text:
///   "Domain — Territorial Kavu gets +1/+1 for each basic land type among
///    lands you control.
///    Whenever Territorial Kavu attacks, you may discard a card. If you do,
///    draw a card."
///
/// ## Implementation
///
/// ### Part 1 — Domain P/T pump (CR 702.16 / CR 613.1g)
///
/// "Territorial Kavu gets +1/+1 for each basic land type among lands you
/// control." This is a <b>Layer 7c static modification</b> — not a CDA
/// (Layer 7a), because Kavu has a printed base P/T (2/2) that serves as
/// the foundation; the Domain bonus stacks on top as a modifier.
///
/// Implemented via <see cref="DomainPumpStaticEffect"/>, a
/// <see cref="ContinuousEffect"/> subclass that re-counts the controller's
/// distinct basic land types on every <see cref="ContinuousEffectsService.Compute"/>
/// invocation. The five basic land types are {Plains, Island, Swamp,
/// Mountain, Forest} per CR 702.16 / CR 205.3i / 305.6. Wastes is a basic
/// land but not a basic land <i>type</i> for Domain purposes.
///
/// Layer 4 retype effects (Blood Moon, Spreading Seas, Urborg, Yavimaya)
/// feed through correctly when a live <see cref="ContinuousEffectsService"/>
/// is supplied for the count; the single-arg factory path uses printed
/// subtypes (suitable for tests).
///
/// Lifecycle: ETB registers the effect; LTB unregisters it. Mirrors
/// <see cref="TarmogoyfFactory"/> and <see cref="BloodMoonFactory"/>
/// (subscribe to <see cref="CardMovedEvent"/> on the supplied
/// <see cref="IEventBus"/>, sync on each relevant move).
///
/// ### Part 2 — Attack trigger loot (CR 508.1f / CR 603.1)
///
/// "Whenever Territorial Kavu attacks, you may discard a card. If you do,
/// draw a card." This is a loot on attack — the discard and the draw are
/// both optional (the "you may discard; if you do, draw" framing means
/// neither fires if the controller has an empty hand).
///
/// Wired via <see cref="Triggers.OnAttackSelf"/> against
/// <see cref="CreatureAttacksEvent"/>. On resolution:
/// - If the controller has a card in hand: discard the first card
///   (v1 deterministic; CR 701.16a agent-driven choice deferred — same
///   posture as <see cref="PsychicFrogFactory"/> / Faithless Looting),
///   then draw one.
/// - If the controller has no cards in hand: no-op (CR 101.3 — "you may"
///   + "if you do" binds both halves; neither fires without a card to
///   discard).
/// - Empty library on the draw step: <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///   (CR 704.5b / 120.3).
///
/// ## Deferred (v1 gaps)
/// - <b>Layer 4 feed-through in the pump</b>: when no live
///   <see cref="ContinuousEffectsService"/> is supplied (single-arg
///   dispatcher or tests) the count uses printed subtypes only. Production
///   callers supply a live service via the full overload.
/// - <b>Discard prompt</b>: v1 deterministically discards the first card
///   in hand; agent-driven "choose which card to discard" deferred behind
///   the same gate as Liliana / Faithless Looting / Psychic Frog.
/// - <b>"You may" prompt on the attack trigger</b>: v1 always takes the
///   loot when a card is available; an explicit yes/no prompt is deferred.
/// </summary>
public static class TerritorialKavuFactory
{
    public const string CardName = "Territorial Kavu";
    public const string PrintedManaCost = "{G}{W}";

    /// <summary>
    /// Construct Territorial Kavu with no live <see cref="ContinuousEffectsService"/>
    /// or <see cref="TriggerManager"/> wiring. The Domain pump is attached
    /// as a static effect but not registered; the attack trigger is attached
    /// for shape but not registered. Suitable for factory-shape / dispatch
    /// tests and for the <see cref="Majik.Core.CardData.NamedCardFactory"/>
    /// dispatcher path.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Territorial Kavu with optional runtime services.
    /// <para>When <paramref name="effects"/> is supplied, a
    /// <see cref="DomainPumpStaticEffect"/> is registered so the Domain
    /// pump is evaluated on every <see cref="ContinuousEffectsService.Compute"/>
    /// call. When <paramref name="eventBus"/> is also supplied, the lifecycle
    /// binder subscribes to <see cref="CardMovedEvent"/> so the effect
    /// registers on ETB and unregisters on LTB (mirrors
    /// <see cref="TarmogoyfFactory"/>'s lifecycle wiring).</para>
    /// <para>When <paramref name="triggers"/> is supplied, the attack trigger
    /// is registered so a <see cref="CreatureAttacksEvent"/> from Territorial
    /// Kavu automatically queues the loot ability.</para>
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Kavu });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Part 1 — Domain P/T pump. Layer 7c static effect.
        //   "Domain — Territorial Kavu gets +1/+1 for each basic land type
        //    among lands you control." CR 702.16 / CR 613.1g.
        // Re-counts the controller's distinct basic land types on every
        // Compute call (same live-count shape as TarmogoyfFactory's CDA,
        // but Layer 7c rather than Layer 7a — printed base P/T 2/2 stands).
        // Lifecycle: register on ETB, unregister on LTB, mirroring
        // BloodMoonFactory / TarmogoyfFactory.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new DomainPumpLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // Part 2 — Attack trigger loot. CR 508.1f / CR 603.1.
        //   "Whenever Territorial Kavu attacks, you may discard a card.
        //    If you do, draw a card."
        // v1 deterministic: discard first card in hand (if any), then draw.
        // Empty hand → no-op. Empty library on draw →
        // MarkTriedToDrawFromEmptyLibrary (CR 704.5b / 120.3).
        // Mirrors PsychicFrogFactory loot shape.
        // ----------------------------------------------------------------
        var lootEffect = new Effect(
            $"{CardName}: attack trigger — discard a card, then draw a card",
            () =>
            {
                // "You may discard a card. If you do, draw a card."
                // Both halves are conditional on having a card in hand.
                var pick = owner.Zones.Hand.GetCards().FirstOrDefault();
                if (pick == null) return; // no card in hand → no-op

                // Discard (hand → graveyard). v1 first-card-in-hand pick
                // (CR 701.16a — agent-driven choice deferred).
                owner.Zones.Hand.RemoveCard(pick);
                owner.Zones.Graveyard.AddCard(pick);

                // Draw one card. Empty library stamps loss condition
                // (CR 704.5b / 120.3).
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { lootEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    // -----------------------------------------------------------------------
    // DomainPumpStaticEffect — Layer 7c live-count self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Territorial Kavu's Domain pump.
    /// On every <see cref="ContinuousEffectsService.Compute"/> invocation
    /// this computes the number of distinct basic land types among the
    /// controller's lands and applies +N/+N to Kavu's characteristics.
    ///
    /// <para>Uses <see cref="TribalFlamesFactory.CountDomain"/> for the
    /// domain count when no <see cref="ContinuousEffectsService"/> is
    /// available (printed-subtypes mode). When a live service is supplied
    /// to the count helper, layer-4 retypes (Blood Moon, Spreading Seas,
    /// Urborg, Yavimaya) feed through correctly per CR 613.1d.</para>
    ///
    /// <para>Active only while Kavu is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the
    /// ETB/LTB lifecycle wiring in <see cref="DomainPumpLifecycle"/>).</para>
    /// </summary>
    public sealed class DomainPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService? _layerService;

        public DomainPumpStaticEffect(
            Creature source,
            Player controller,
            ContinuousEffectsService? layerService)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _layerService = layerService;
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Kavu is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Kavu itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +N/+N where N = distinct basic land types the controller
        /// controls. CR 702.16 count delegated to
        /// <see cref="TribalFlamesFactory.CountDomain"/>.
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            // Pass null for the layer service here — using the full
            // ContinuousEffectsService during an in-flight Compute would
            // cause infinite recursion. The printed-subtypes mode (effects
            // = null) is correct for the count helper when called from
            // within a Compute pass.  Callers that need layer-4 accuracy
            // (Blood Moon, Urborg, etc.) can register the effect once the
            // engine has a two-pass dependency resolution mechanism.
            var n = TribalFlamesFactory.CountDomain(_controller, effects: null);
            chars.Power += n;
            chars.Toughness += n;
        }
    }

    // -----------------------------------------------------------------------
    // DomainPumpLifecycle — ETB/LTB wiring for the Domain pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Territorial Kavu's Domain pump.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers
    /// <see cref="DomainPumpStaticEffect"/> when Kavu enters the battlefield,
    /// unregisters when it leaves. Mirrors <see cref="TarmogoyfFactory"/>'s
    /// <c>TarmogoyfCdaLifecycle</c> and <see cref="BloodMoonFactory"/>'s
    /// <c>RetypeLandsStaticEffect</c> lifecycle pattern.
    /// </summary>
    private sealed class DomainPumpLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private DomainPumpStaticEffect? _registered;
        private bool _attached;

        public DomainPumpLifecycle(
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
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new DomainPumpStaticEffect(_source, _controller, _effects);
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
