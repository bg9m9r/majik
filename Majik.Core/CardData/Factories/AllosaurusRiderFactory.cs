using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Allosaurus Rider (Coldsnap, {5}{G}{G}).
///
/// Creature — Elf Warrior
/// Oracle text:
///   "You may exile two green cards from your hand rather than pay this
///    spell's mana cost.
///    Allosaurus Rider's power and toughness are each equal to 1 plus
///    the number of lands you control."
///
/// ## Implementation
///
/// ### Card identity
/// Elf Warrior, {5}{G}{G}, mana value 7. Green card. Printed P/T 1+*/1+*
/// (base 0/0 — Layer 7a CDA overwrites on every Compute).
///
/// ### Alternative cost (CR 117.11 / CR 701.21)
/// "Exile two green cards from your hand" — NO mana-value restriction,
/// just any two green cards. Callers supply
/// <see cref="Majik.Core.Costs.ExileTwoColoredCardsAlternativeCost"/>
/// with <see cref="ManaColor.Green"/> + two distinct green hand cards to
/// <see cref="SpellCastFlow.CastAsync"/>; the cost exiles both on
/// resolution.
///
/// ### Variable P/T — Layer 7a CDA (CR 604.3 / CR 613.2)
/// Power = toughness = 1 + (number of lands controller controls at compute
/// time). Implemented via <see cref="CdaPowerToughnessEffect"/>; the
/// evaluator reads controller's battlefield live on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> — no caching
/// or event subscriptions needed.
///
/// Lifecycle mirrors <see cref="TarmogoyfFactory"/>: an inner
/// <see cref="AllosaurusRiderCdaLifecycle"/> subscribes to
/// <see cref="CardMovedEvent"/>; registers the CDA when Allosaurus Rider
/// enters the battlefield, unregisters when it leaves.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so
/// the CDA is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity but no live CDA — suitable for shape tests.
/// </summary>
[CardName("Allosaurus Rider")]
public static class AllosaurusRiderFactory
{
    public const string CardName = "Allosaurus Rider";
    public const string PrintedManaCost = "{5}{G}{G}";

    /// <summary>
    /// Creates Allosaurus Rider with correct card identity only (no live
    /// Layer 7a CDA). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Allosaurus Rider. When <paramref name="effects"/>
    /// is supplied, a <see cref="CdaPowerToughnessEffect"/> is attached so
    /// the Layer 7a CDA registers/unregisters as Allosaurus Rider enters/
    /// leaves the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="effects"/> is null
    /// the lifecycle wiring is silently skipped (matches the shape-only
    /// overload).
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the CDA
    /// against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be null —
    /// the CDA's <see cref="CdaPowerToughnessEffect.IsActive"/> gate covers
    /// correctness, but no explicit unregister will fire.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed P/T = 1+*/1+* (CDA will overwrite when active).
        // CR 208.2c — printed value treated as 0 when CDA defines the value;
        // we use 0/0 as seed values since Layer 7a overwrites on every Compute.
        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 0,
            toughness: 0,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new AllosaurusRiderCdaLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Compute the number of lands the controller controls at compute time.
    /// Pure helper exposed for tests.
    /// </summary>
    public static int CountControllerLands(Creature rider)
    {
        ArgumentNullException.ThrowIfNull(rider);
        return rider.Controller?.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Land)) ?? 0;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Allosaurus Rider's CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Allosaurus Rider enters
    /// the battlefield, unregisters when it leaves. Mirrors
    /// <see cref="TarmogoyfFactory"/>'s inner lifecycle class.
    /// </summary>
    private sealed class AllosaurusRiderCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public AllosaurusRiderCdaLifecycle(
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
                    powerOf: _ => AllosaurusRiderFactory.CountControllerLands(_source) + 1,
                    toughnessOf: _ => AllosaurusRiderFactory.CountControllerLands(_source) + 1);
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
