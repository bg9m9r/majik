using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wild Nacatl (Alara Reborn, {G}).
///
/// Creature — Cat Warrior 1/1. Oracle text:
///   "Wild Nacatl gets +1/+1 as long as you control a Mountain.
///    Wild Nacatl gets +1/+1 as long as you control a Plains."
///
/// ## Implementation
///
/// Two independent Layer 7c static self-pumps (CR 613.1g / 613.3) — one
/// gated on "you control a Mountain", the other on "you control a Plains".
/// Both can stack, giving the iconic 3/3-for-{G} statline when the
/// controller has both basics on the battlefield.
///
/// This is a static <b>conditional</b> pump (printed base P/T 1/1 stands
/// as the foundation), not a CDA — the same shape as
/// <see cref="TerritorialKavuFactory"/>'s Domain pump but with a boolean
/// land-type predicate instead of a domain count. Each pump's
/// <see cref="ContinuousEffect.AppliesTo"/> targets Wild Nacatl itself;
/// <see cref="ContinuousEffect.IsActive"/> is the battlefield gate.
///
/// Lifecycle (register on ETB / unregister on LTB) mirrors
/// <see cref="TarmogoyfFactory"/> / <see cref="TerritorialKavuFactory"/>:
/// subscribe to <see cref="CardMovedEvent"/> on the supplied
/// <see cref="IEventBus"/>; register both pumps when Nacatl enters the
/// battlefield, unregister when it leaves.
///
/// Layer 4 retypes (Blood Moon, Spreading Seas, Urborg, Yavimaya) feed
/// through correctly when the predicate is checked against the live
/// continuous-effects pipeline. The single-arg factory path uses printed
/// subtypes (suitable for shape / dispatcher tests).
///
/// ## Deferred (v1 gaps)
/// - <b>Layer-4 feed-through on the predicate</b>: to avoid recursion
///   during an in-flight Compute pass, the controls-a-basic-land-type
///   predicate uses printed subtypes (mirrors
///   <see cref="TerritorialKavuFactory.DomainPumpStaticEffect.Apply"/>'s
///   posture). Same gating awaits a two-pass dependency resolution.
/// </summary>
[CardName("Wild Nacatl")]
public static class WildNacatlFactory
{
    public const string CardName = "Wild Nacatl";
    public const string PrintedManaCost = "{G}";

    /// <summary>
    /// Construct Wild Nacatl with no live <see cref="ContinuousEffectsService"/>
    /// wiring. Suitable for factory-shape / dispatcher tests; the conditional
    /// pumps are not registered.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Wild Nacatl with optional runtime services. When
    /// <paramref name="effects"/> is supplied, two
    /// <see cref="ConditionalPumpStaticEffect"/> instances (one keyed on
    /// Mountain, one keyed on Plains) are registered so the +1/+1 pumps
    /// are evaluated on every <see cref="ContinuousEffectsService.Compute"/>
    /// call. When <paramref name="eventBus"/> is also supplied, the
    /// lifecycle binder subscribes to <see cref="CardMovedEvent"/> so
    /// both effects register on ETB and unregister on LTB.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Two conditional Layer 7c +1/+1 pumps — Mountain & Plains.
        // CR 613.1g / 702.16 land-type predicate. Each gate is independent;
        // controlling both lands stacks both pumps for a 3/3.
        // Lifecycle: register both on ETB, unregister both on LTB. Mirrors
        // TarmogoyfFactory / TerritorialKavuFactory.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new ConditionalPumpLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Returns true when <paramref name="controller"/> controls at least one
    /// land with the supplied basic land subtype (CR 205.3i / 305.6) on the
    /// battlefield. Uses printed subtypes — Layer-4 retypes (Blood Moon,
    /// Urborg, Yavimaya) deferred (same posture as
    /// <see cref="TerritorialKavuFactory.DomainPumpStaticEffect.Apply"/>).
    /// </summary>
    public static bool ControlsBasicLandType(Player controller, CardSubtype landType)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card is not Land land) continue;
            if (land.HasSubtype(landType)) return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // ConditionalPumpStaticEffect — Layer 7c gated +1/+1 self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect: +1/+1 to Wild Nacatl while the controller
    /// controls a land with the bound basic land type. Two instances of this
    /// effect are registered for Wild Nacatl (one for Mountain, one for
    /// Plains); both can apply simultaneously.
    /// </summary>
    public sealed class ConditionalPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly CardSubtype _landType;

        public ConditionalPumpStaticEffect(
            Creature source,
            Player controller,
            CardSubtype landType)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _landType = landType;
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Wild Nacatl is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Wild Nacatl itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +1/+1 iff the controller controls a land with the bound
        /// basic land subtype.
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            if (!WildNacatlFactory.ControlsBasicLandType(_controller, _landType)) return;
            chars.Power += 1;
            chars.Toughness += 1;
        }
    }

    // -----------------------------------------------------------------------
    // ConditionalPumpLifecycle — ETB/LTB wiring for both pumps.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Wild Nacatl's two conditional pumps.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers the Mountain
    /// and Plains pumps when Nacatl enters the battlefield, unregisters
    /// both when it leaves. Mirrors <see cref="TerritorialKavuFactory"/>'s
    /// <c>DomainPumpLifecycle</c>.
    /// </summary>
    private sealed class ConditionalPumpLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private ConditionalPumpStaticEffect? _mountainPump;
        private ConditionalPumpStaticEffect? _plainsPump;
        private bool _attached;

        public ConditionalPumpLifecycle(
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
            if (shouldBeActive && _mountainPump == null)
            {
                _mountainPump = new ConditionalPumpStaticEffect(
                    _source, _controller, CardSubtype.Mountain);
                _plainsPump = new ConditionalPumpStaticEffect(
                    _source, _controller, CardSubtype.Plains);
                _effects.Register(_mountainPump);
                _effects.Register(_plainsPump);
            }
            else if (!shouldBeActive && _mountainPump != null)
            {
                _effects.Unregister(_mountainPump);
                _effects.Unregister(_plainsPump!);
                _mountainPump = null;
                _plainsPump = null;
            }
        }
    }
}
