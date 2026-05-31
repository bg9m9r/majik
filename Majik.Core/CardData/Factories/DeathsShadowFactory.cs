using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Death's Shadow (Worldwake / Modern Horizons 2
/// reprint).
///
/// Creature — Avatar, {B}
/// Oracle text: "Death's Shadow gets -X/-X, where X is your life total."
///
/// ## Implementation
///
/// CR 604.3 / 613.2 — modeled as a Layer 7a characteristic-defining ability
/// that sets P/T to <c>max(0, 13 - controllerLife)</c>. The printed text uses
/// the "-X/-X" Layer 7c framing, but the equivalent CDA shape (current
/// printings use "Death's Shadow's power and toughness are each equal to 13
/// minus your life total") collapses the math into a single 7a write — no
/// dependency on the base value, no 7c interaction with anthems / counters
/// being subtracted from. We pick the CDA shape because <see cref="CdaPowerToughnessEffect"/>
/// is the established Layer 7a primitive (PR #173) and it keeps the math
/// behind one evaluator.
///
/// Clamp: <c>max(0, 13 - life)</c>. With life &gt; 13 the CDA yields 0/0; the
/// 0-toughness state-based action (CR 704.5f) handles "dies" in real gameplay.
/// With life ≤ 0 the value would exceed 13, but CR 208.2 / canonical printings
/// treat the CDA value as the printed P/T (13/13) and the math <c>13 - life</c>
/// reaches exactly 13 at life = 0; we additionally clamp the upper bound at
/// the printed 13 to handle negative-life edge cases (extra-life loss between
/// SBA checks, untracked life manipulation).
///
/// Printed P/T is 13/13 (CR 208.2c — the CDA defines the value; we keep
/// <c>BasePower=13, BaseToughness=13</c> as seed values that Layer 7a will
/// overwrite on every <see cref="ContinuousEffectsService.Compute(Permanent)"/>).
///
/// Layer 7c +1/+1 / -1/-1 counters and anthems stack on top of the 7a value.
///
/// Lifecycle (register on ETB / unregister on LTB) mirrors
/// <see cref="TarmogoyfFactory"/>: subscribe to <see cref="CardMovedEvent"/>
/// on the supplied <see cref="IEventBus"/>; register the CDA when Death's
/// Shadow enters the battlefield, unregister when it leaves. The
/// <see cref="CdaPowerToughnessEffect.IsActive"/> battlefield gate is a
/// belt-and-braces redundancy when no event bus is supplied.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so the
/// CDA is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card with
/// correct identity but no live CDA — suitable for pure card-shape tests.
/// </summary>
[CardName("Death's Shadow")]
public static class DeathsShadowFactory
{
    public const string CardName = "Death's Shadow";
    public const string Cost = "{B}";
    public const int PrintedPower = 13;
    public const int PrintedToughness = 13;

    /// <summary>
    /// Creates a Death's Shadow with correct card identity only (no live
    /// Layer 7a CDA). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Death's Shadow. When <paramref name="effects"/>
    /// is supplied, a <see cref="CdaPowerToughnessEffect"/> is attached so
    /// the Layer 7a CDA registers/unregisters as Death's Shadow enters/leaves
    /// the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="effects"/> is null
    /// the lifecycle wiring is silently skipped (matches the shape-only
    /// overload).
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the
    /// CDA against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be
    /// null — the CDA's <see cref="CdaPowerToughnessEffect.IsActive"/>
    /// gate covers correctness, but no explicit unregister will fire.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed P/T = 13/13 (the CDA will overwrite when active).
        var card = new Creature(
            CardName,
            Cost,
            power: PrintedPower,
            toughness: PrintedToughness,
            subtypes: new[] { CardSubtype.Avatar });
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new DeathsShadowCdaLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Pure helper: compute Death's Shadow's CDA P/T value for the supplied
    /// life total. Both power and toughness use the same value. Clamped to
    /// <c>[0, 13]</c> — life ≥ 13 floors to 0 (state-based "dies" applies in
    /// real gameplay), life ≤ 0 caps at the printed 13.
    /// </summary>
    public static int ComputePT(int controllerLife)
    {
        var value = PrintedPower - controllerLife;
        if (value < 0) return 0;
        if (value > PrintedPower) return PrintedPower;
        return value;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Death's Shadow's CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Death's Shadow enters the
    /// battlefield, unregisters when it leaves. Mirrors the structure of
    /// <see cref="TarmogoyfFactory"/>'s lifecycle binder.
    /// </summary>
    private sealed class DeathsShadowCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public DeathsShadowCdaLifecycle(
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
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    powerOf: src => ComputePT(src.Controller?.LifeTotal ?? 0),
                    toughnessOf: src => ComputePT(src.Controller?.LifeTotal ?? 0));
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
