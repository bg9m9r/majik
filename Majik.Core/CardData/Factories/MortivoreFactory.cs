using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mortivore (Odyssey, {3}{B}{B}).
///
/// Creature — Lhurgoyf. Oracle text:
///   "Mortivore's power and toughness are each equal to the number of
///    creature cards in all graveyards.
///    {B}: Regenerate Mortivore."
///
/// ## Implementation
///
/// CR 604.3 / 613.2 — a characteristic-defining ability that sets P/T in
/// Layer 7a. Implemented via <see cref="CdaPowerToughnessEffect"/> whose
/// power AND toughness evaluators each return the same value: the count
/// of creature cards (<see cref="CardType.Creature"/>) across every
/// graveyard in the game. Shape mirrors <see cref="TarmogoyfFactory"/>'s
/// cross-game graveyard scan, swapping the distinct-type set count for a
/// linear "is creature card" tally.
///
/// Printed P/T is */* (CR 208.2c — when a CDA defines a value the printed
/// value is treated as that value); we seed <c>BasePower=0,
/// BaseToughness=0</c> as harmless placeholders since Layer 7a will
/// overwrite them on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>.
///
/// ## {B}: Regenerate Mortivore (CR 701.18 / 701.15a)
///
/// Wired as an <see cref="ActivatedAbility"/> whose sole cost is
/// <see cref="ManaCostCost"/> <c>{B}</c>. Resolution calls
/// <see cref="Permanent.AddRegenerationShield"/> on Mortivore — the next
/// time Mortivore would be destroyed this turn the shield consumes the
/// destroy, taps Mortivore, and clears damage (CR 701.15c). Shields
/// stack across multiple activations and clear during cleanup (CR 514.2).
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="TarmogoyfFactory"/>'s
/// <c>TarmogoyfCdaLifecycle</c>: subscribe to <see cref="CardMovedEvent"/>
/// on the supplied <see cref="IEventBus"/>; register the CDA when
/// Mortivore enters the battlefield, unregister when it leaves. The
/// <see cref="CdaPowerToughnessEffect.IsActive"/> battlefield gate is a
/// belt-and-braces redundancy if no event bus is supplied.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?, Func{IEnumerable{ICard}})"/>
/// so the CDA is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity (and the {B}: regenerate ability) but no live
/// CDA — suitable for pure card-shape tests.
/// </summary>
[CardName("Mortivore")]
public static class MortivoreFactory
{
    public const string CardName = "Mortivore";
    public const string Cost = "{3}{B}{B}";

    /// <summary>
    /// Creates a Mortivore with correct card identity + the {B}:
    /// regenerate activated ability, but no live Layer 7a CDA. Suitable
    /// for factory-shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null, graveyardSource: null);

    /// <summary>
    /// Creates a fully-wired Mortivore. When <paramref name="effects"/>
    /// and <paramref name="graveyardSource"/> are supplied, a
    /// <see cref="CdaPowerToughnessEffect"/> is attached so the Layer 7a
    /// CDA registers/unregisters as Mortivore enters/leaves the
    /// battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>.
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the
    /// CDA against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be
    /// null — the CDA's <see cref="CdaPowerToughnessEffect.IsActive"/>
    /// gate covers correctness, but no explicit unregister will fire.</param>
    /// <param name="graveyardSource">Closure returning every card in
    /// every graveyard in the game (typically
    /// <c>() =&gt; players.SelectMany(p =&gt; p.Zones.Graveyard.GetCards())</c>).
    /// Read fresh on every Compute. Pass null for shape-only.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? graveyardSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed P/T = */* (CDA-defined); seed 0/0 placeholders since
        // Layer 7a will overwrite them on every Compute.
        var card = new Creature(
            CardName,
            Cost,
            power: 0,
            toughness: 0,
            subtypes: new[] { CardSubtype.Lhurgoyf });
        card.SetOwner(owner);
        card.SetController(owner);

        // ---------------------------------------------------------------
        // {B}: Regenerate Mortivore.
        // CR 701.18 — "Regenerate [self]" = create a regeneration shield
        // on the target (CR 701.15a). Activated ability, regular speed,
        // any number of times per turn (shields stack and clear at EOT).
        // ---------------------------------------------------------------
        var regenerateEffect = new Effect(
            $"{CardName}: regenerate self",
            () => card.AddRegenerationShield());

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{B}") },
            effects: new IEffect[] { regenerateEffect }));

        // ---------------------------------------------------------------
        // Layer 7a CDA P/T lifecycle wiring.
        // ---------------------------------------------------------------
        if (effects != null && graveyardSource != null)
        {
            var lifecycle = new MortivoreCdaLifecycle(
                card,
                effects,
                eventBus,
                graveyardSource);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Count creature cards (<see cref="CardType.Creature"/>) across the
    /// supplied graveyard cards. Pure helper exposed for tests; mirrors
    /// the closure baked into the live
    /// <see cref="CdaPowerToughnessEffect"/>.
    /// </summary>
    public static int CountCreatureCards(IEnumerable<ICard> graveyardCards)
    {
        ArgumentNullException.ThrowIfNull(graveyardCards);
        var count = 0;
        foreach (var card in graveyardCards)
        {
            if (card.HasType(CardType.Creature)) count++;
        }
        return count;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Mortivore's CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Mortivore enters the
    /// battlefield, unregisters when it leaves. Mirrors the structure of
    /// <c>TarmogoyfCdaLifecycle</c> — only the count closure differs
    /// (creature-card tally instead of distinct-type set).
    /// </summary>
    private sealed class MortivoreCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _graveyardSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public MortivoreCdaLifecycle(
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
                    powerOf: _ => MortivoreFactory.CountCreatureCards(_graveyardSource()),
                    toughnessOf: _ => MortivoreFactory.CountCreatureCards(_graveyardSource()));
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
