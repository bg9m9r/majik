using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tarmogoyf (Future Sight / Modern Masters reprints).
///
/// Creature — Lhurgoyf, {1}{G}
/// Oracle text: "Tarmogoyf's power is equal to the number of card types
/// among cards in all graveyards, and its toughness is equal to that
/// number plus 1."
///
/// ## Implementation
///
/// CR 604.3 / 613.2 — a characteristic-defining ability that sets P/T in
/// Layer 7a. Implemented via <see cref="CdaPowerToughnessEffect"/> whose
/// power evaluator counts the distinct <see cref="CardType"/> values
/// across every card in every graveyard in the game, and whose toughness
/// evaluator returns that count plus 1.
///
/// Printed P/T is 0/1 (CR 208.2c — when a CDA defines a value, the
/// printed value is treated as that value; we keep <c>BasePower=0,
/// BaseToughness=1</c> as harmless seed values, since Layer 7a will
/// overwrite them on every <see cref="ContinuousEffectsService.Compute(Permanent)"/>).
///
/// Cross-game graveyard access: the factory binds a
/// <see cref="Func{TResult}"/> closure over the supplied
/// <c>graveyardSource</c> (typically <c>() =&gt; players.SelectMany(p
/// =&gt; p.Zones.Graveyard.GetCards())</c>). The CDA is evaluated every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>, so the
/// closure reads live graveyard state at lookup time — no caching, no
/// event subscriptions needed for the count itself.
///
/// Lifecycle (register on ETB / unregister on LTB) mirrors
/// <see cref="BloodMoonFactory"/> and <see cref="RetypeLandsStaticEffect"/>:
/// subscribe to <see cref="CardMovedEvent"/> on the supplied
/// <see cref="IEventBus"/>; register the CDA when Tarmogoyf enters the
/// battlefield, unregister when it leaves. The
/// <see cref="CdaPowerToughnessEffect.IsActive"/> battlefield gate is a
/// belt-and-braces redundancy if no event bus is supplied.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?, Func{IEnumerable{ICard}})"/>
/// so the CDA is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity but no live CDA — suitable for pure card-shape
/// tests.
/// </summary>
[CardName("Tarmogoyf")]
public static class TarmogoyfFactory
{
    public const string CardName = "Tarmogoyf";
    public const string Cost = "{1}{G}";

    /// <summary>
    /// Creates a Tarmogoyf with correct card identity only (no live
    /// Layer 7a CDA). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null, graveyardSource: null);

    /// <summary>
    /// Creates a fully-wired Tarmogoyf. When <paramref name="effects"/> and
    /// <paramref name="graveyardSource"/> are supplied, a
    /// <see cref="CdaPowerToughnessEffect"/> is attached so the Layer 7a
    /// CDA registers/unregisters as Tarmogoyf enters/leaves the
    /// battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When either of those is null the
    /// lifecycle wiring is silently skipped (matches the shape-only
    /// overload).
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the
    /// CDA against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be
    /// null — the CDA's <see cref="CdaPowerToughnessEffect.IsActive"/>
    /// gate covers correctness, but no explicit unregister will fire.</param>
    /// <param name="graveyardSource">Closure returning every card in
    /// every graveyard in the game. Read fresh on every Compute. Pass
    /// null for shape-only.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? graveyardSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed P/T = 0/1 (the CDA will overwrite when active).
        var card = new Creature(
            CardName,
            Cost,
            power: 0,
            toughness: 1,
            subtypes: new[] { CardSubtype.Lhurgoyf });
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null && graveyardSource != null)
        {
            var lifecycle = new TarmogoyfCdaLifecycle(
                card,
                effects,
                eventBus,
                graveyardSource);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Compute the distinct <see cref="CardType"/> count across the
    /// supplied graveyard cards. Pure helper exposed for tests; mirrors
    /// the closure baked into the live <see cref="CdaPowerToughnessEffect"/>.
    /// </summary>
    public static int CountDistinctCardTypes(IEnumerable<ICard> graveyardCards)
    {
        ArgumentNullException.ThrowIfNull(graveyardCards);
        var types = new HashSet<CardType>();
        foreach (var card in graveyardCards)
        {
            foreach (var t in card.CardTypes)
            {
                types.Add(t);
            }
        }
        return types.Count;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Tarmogoyf's CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Tarmogoyf enters the
    /// battlefield, unregisters when it leaves. Mirrors the structure of
    /// <see cref="RetypeLandsStaticEffect"/>.
    /// </summary>
    private sealed class TarmogoyfCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _graveyardSource;
        private readonly Action<GameEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public TarmogoyfCdaLifecycle(
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
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    powerOf: _ => TarmogoyfFactory.CountDistinctCardTypes(_graveyardSource()),
                    toughnessOf: _ => TarmogoyfFactory.CountDistinctCardTypes(_graveyardSource()) + 1);
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
