using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nighthawk Scavenger (Zendikar Rising, {1}{B}{B}).
///
/// Creature — Vampire Rogue, printed P/T 1+*/3. Oracle text:
///   "Flying, deathtouch, lifelink
///    Nighthawk Scavenger's power is equal to 1 plus the number of card
///    types among cards in your opponents' graveyards."
///
/// ## Implementation
///
/// This card combines two existing engine patterns:
///
/// 1. <b>Keyword shell</b> — Flying (CR 702.9), Deathtouch (CR 702.2), and
///    Lifelink (CR 702.15) attached as <see cref="KeywordAbility"/> markers,
///    mirroring <see cref="VampireNighthawkFactory"/>. Combat reads these.
///
/// 2. <b>Characteristic-defining power</b> (CR 604.3 / 613.2 — Layer 7a) via
///    <see cref="CdaPowerToughnessEffect"/>, mirroring
///    <see cref="TarmogoyfFactory"/>. The power evaluator returns
///    <c>1 + (distinct card-type count across the supplied graveyard
///    cards)</c>. Two differences from Tarmogoyf:
///    <list type="bullet">
///      <item>The base is <c>1</c> ("1 plus the number…").</item>
///      <item>Only the controller's OPPONENTS' graveyards are counted — the
///        caller supplies a closure scoped to those graveyards (typically
///        <c>() =&gt; opponents.SelectMany(p =&gt; p.Zones.Graveyard.GetCards())</c>).</item>
///    </list>
///    Toughness is the fixed printed <c>3</c> — it is NOT a CDA, so the
///    toughness evaluator just returns 3. (We still drive it through the
///    Layer-7a effect so the 7a SET semantics apply uniformly and later
///    7c pumps / counters stack on top per CR 613.7.)
///
/// Printed P/T is 1/3 (CR 208.2c — the printed power is treated as the CDA's
/// value; we keep <c>BasePower=1, BaseToughness=3</c> as harmless seed
/// values that Layer 7a overwrites on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>).
///
/// Lifecycle (register on ETB / unregister on LTB) mirrors
/// <see cref="TarmogoyfFactory"/>: subscribe to <see cref="CardMovedEvent"/>
/// on the supplied <see cref="IEventBus"/>; register the CDA when the
/// creature enters the battlefield, unregister when it leaves. The
/// <see cref="CdaPowerToughnessEffect.IsActive"/> battlefield gate is a
/// belt-and-braces redundancy if no event bus is supplied.
///
/// The single-argument <see cref="Create(Player)"/> overload produces a
/// card with correct identity + keywords but no live CDA — suitable for
/// pure card-shape tests and for the test-only
/// <see cref="NamedCardFactory"/> dispatch path.
/// </summary>
[CardName("Nighthawk Scavenger")]
public static class NighthawkScavengerFactory
{
    public const string CardName = "Nighthawk Scavenger";
    public const string Cost = "{1}{B}{B}";

    /// <summary>
    /// Creates a Nighthawk Scavenger with correct card identity + keyword
    /// markers only (no live Layer 7a CDA). Suitable for factory-shape /
    /// naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null, opponentGraveyardSource: null);

    /// <summary>
    /// Creates a fully-wired Nighthawk Scavenger. When <paramref name="effects"/>
    /// and <paramref name="opponentGraveyardSource"/> are supplied, a
    /// <see cref="CdaPowerToughnessEffect"/> is attached so the Layer 7a CDA
    /// registers/unregisters as the creature enters/leaves the battlefield
    /// via <see cref="CardMovedEvent"/> on <paramref name="eventBus"/>. When
    /// either of those is null the lifecycle wiring is silently skipped.
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the CDA
    /// against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be null.</param>
    /// <param name="opponentGraveyardSource">Closure returning every card in
    /// every graveyard owned by the controller's OPPONENTS. Read fresh on
    /// every Compute. Pass null for shape-only.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? opponentGraveyardSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed P/T = 1/3 (the CDA overwrites power when active; toughness
        // is fixed at 3).
        var card = new Creature(
            CardName,
            Cost,
            power: 1,
            toughness: 3,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Rogue });
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by combat.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch reads it.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));
        // CR 702.15 — Lifelink marker. CombatAbilities.HasLifelink reads it.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        if (effects != null && opponentGraveyardSource != null)
        {
            var lifecycle = new ScavengerCdaLifecycle(
                card,
                effects,
                eventBus,
                opponentGraveyardSource);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Compute the CDA power: <c>1 + (distinct <see cref="CardType"/> count
    /// across the supplied opponents'-graveyard cards)</c>. Pure helper
    /// exposed for tests; mirrors the closure baked into the live
    /// <see cref="CdaPowerToughnessEffect"/>.
    /// </summary>
    public static int ComputePower(IEnumerable<ICard> opponentGraveyardCards)
    {
        ArgumentNullException.ThrowIfNull(opponentGraveyardCards);
        var types = new HashSet<CardType>();
        foreach (var card in opponentGraveyardCards)
        {
            foreach (var t in card.CardTypes)
            {
                types.Add(t);
            }
        }
        return 1 + types.Count;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for the CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when the creature enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="TarmogoyfFactory"/>'s lifecycle.
    /// </summary>
    private sealed class ScavengerCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _opponentGraveyardSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public ScavengerCdaLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<IEnumerable<ICard>> opponentGraveyardSource)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _opponentGraveyardSource = opponentGraveyardSource;
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
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    // CR 604.3 — power = 1 + opponents'-graveyard card types.
                    powerOf: _ => ComputePower(_opponentGraveyardSource()),
                    // Toughness is the fixed printed 3 (not a CDA).
                    toughnessOf: _ => 3);
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
