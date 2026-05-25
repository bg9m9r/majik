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
/// Named-card factory for Nethergoyf (Modern Horizons 3, {B}).
///
/// Creature — Lhurgoyf. Oracle text:
///   "Nethergoyf's power is equal to the number of card types among cards
///    in your graveyard and its toughness is equal to that number plus 1.
///    Escape—{2}{B}, Exile any number of other cards from your graveyard
///    with four or more card types among them. (You may cast this card
///    from your graveyard for its escape cost.)"
///
/// ## Implementation
///
/// CR 604.3 / 613.2 — a characteristic-defining ability that sets P/T in
/// Layer 7a. Implemented via <see cref="CdaPowerToughnessEffect"/> whose
/// power evaluator counts the distinct <see cref="CardType"/> values
/// across the cards in <em>Nethergoyf's controller's</em> graveyard
/// (narrower than <see cref="TarmogoyfFactory"/>'s "all graveyards"
/// scan), and whose toughness evaluator returns that count plus 1.
///
/// Shape closely mirrors <see cref="TarmogoyfFactory"/> — the only
/// behavioural delta is the graveyard source closure, which is bound to
/// the live <see cref="Card.Controller"/> at evaluate time (CR 614.6 —
/// controller is assessed on the current battlefield state on every
/// Compute, so a control-change re-points the scan automatically).
///
/// Printed P/T is 0/1 (CR 208.2c — when a CDA defines a value, the
/// printed value is treated as that value; we keep <c>BasePower=0,
/// BaseToughness=1</c> as harmless seed values, since Layer 7a will
/// overwrite them on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>).
///
/// Lifecycle (register on ETB / unregister on LTB) mirrors
/// <see cref="TarmogoyfFactory"/>'s <c>TarmogoyfCdaLifecycle</c>:
/// subscribe to <see cref="CardMovedEvent"/> on the supplied
/// <see cref="IEventBus"/>; register the CDA when Nethergoyf enters the
/// battlefield, unregister when it leaves. The
/// <see cref="CdaPowerToughnessEffect.IsActive"/> battlefield gate is a
/// belt-and-braces redundancy if no event bus is supplied.
///
/// ## Escape (CR 702.138) — wired via <see cref="EscapeAlternativeCost"/>
///
/// Cast-from-graveyard alt cost with the printed mana payment
/// <c>{2}{B}</c>. The printed exile rider is "exile any number of other
/// cards from your graveyard with four or more card types <em>among
/// them</em>" — a card-type-collective predicate distinct from the
/// fixed-N riders that <see cref="EscapeAlternativeCost"/> currently
/// supports. v1 ships a fixed exile count of 4 (the minimum legal pick
/// that the four-or-more-types rider can satisfy when the graveyard
/// happens to hold four monotype cards). The richer
/// "any-number-with-N-types-among-them" predicate is deferred — see gaps
/// below; same posture as Cabal Therapy's flashback-sacrifice rider
/// being shipped paired-and-noted.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Escape exile rider's "any-number with 4+ types among them"
///   predicate</b>: the v1 fixed-4 exile count is observationally close
///   (four monotype cards in the graveyard satisfy the rider), but the
///   "any-number" surface needs an agent-pick over graveyard subsets
///   that collectively reach the 4-type threshold. Blocked on the same
///   agent-driven exile-subset prompt that Cabal Therapy / Tasigur's
///   "any number" graveyard surfaces need.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/>
/// so the CDA is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity but no live CDA — suitable for pure card-shape
/// tests.
/// </summary>
[CardName("Nethergoyf")]
public static class NethergoyfFactory
{
    public const string CardName = "Nethergoyf";
    public const string Cost = "{B}";

    /// <summary>CR 702.138 — printed Escape mana cost: {2}{B}.</summary>
    public const string EscapeManaCost = "{2}{B}";

    /// <summary>
    /// CR 702.138a — v1 fixed exile count for Nethergoyf's escape rider.
    /// The printed text is "exile any number of other cards from your
    /// graveyard with four or more card types <em>among them</em>"; v1
    /// ships a fixed N=4 stub (see class xmldoc for the deferred
    /// any-number / type-collective predicate gap).
    /// </summary>
    public const int EscapeExileCount = 4;

    /// <summary>
    /// CR 702.138 — Nethergoyf's printed Escape alt-cost ({2}{B},
    /// exile four OTHER graveyard cards in v1; see class xmldoc for the
    /// any-number / 4-types-among-them deferred surface).
    /// </summary>
    public static EscapeAlternativeCost BuildAlternativeCost() =>
        new(ManaCost.Parse(EscapeManaCost), EscapeExileCount);

    /// <summary>
    /// Creates a Nethergoyf with correct card identity only (no live
    /// Layer 7a CDA). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Nethergoyf. When <paramref name="effects"/>
    /// is supplied, a <see cref="CdaPowerToughnessEffect"/> is attached
    /// so the Layer 7a CDA registers/unregisters as Nethergoyf enters /
    /// leaves the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. The CDA scans only the controller's
    /// graveyard (re-read live on every Compute via
    /// <see cref="Card.Controller"/>).
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

        // Printed P/T = 0/1 (the CDA will overwrite when active).
        var card = new Creature(
            CardName,
            Cost,
            power: 0,
            toughness: 1,
            subtypes: new[] { CardSubtype.Lhurgoyf });
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new NethergoyfCdaLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Sample Nethergoyf's CDA against a specific controller's graveyard.
    /// Pure helper exposed for tests; mirrors the closure baked into the
    /// live <see cref="CdaPowerToughnessEffect"/>. Reuses
    /// <see cref="TarmogoyfFactory.CountDistinctCardTypes"/> for the
    /// distinct-type set arithmetic.
    /// </summary>
    public static int CountDistinctCardTypesInControllerGraveyard(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return TarmogoyfFactory.CountDistinctCardTypes(
            controller.Zones.Graveyard.GetCards());
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Nethergoyf's CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Nethergoyf enters the
    /// battlefield, unregisters when it leaves. Mirrors the structure of
    /// <c>TarmogoyfCdaLifecycle</c> — only the graveyard-source closure
    /// differs (live controller's graveyard, re-read each Compute).
    /// </summary>
    private sealed class NethergoyfCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public NethergoyfCdaLifecycle(
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

        private int CountForLiveController()
        {
            // CR 614.6 — controller is assessed on the live battlefield
            // state at evaluate time. Fall back to Owner when Controller
            // is not yet set (defensive — Nethergoyf is always wired with
            // SetController on construction, but the CDA may briefly
            // sample mid-zone-move).
            var controller = _source.Controller ?? _source.Owner;
            if (controller is null) return 0;
            return CountDistinctCardTypesInControllerGraveyard(controller);
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    powerOf: _ => CountForLiveController(),
                    toughnessOf: _ => CountForLiveController() + 1);
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
