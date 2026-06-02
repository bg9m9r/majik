using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Enigma Drake (Dragons of Tarkir, {1}{U}{R}).
///
/// Creature — Drake, */4. Oracle text:
///   "Flying
///    Enigma Drake's power is equal to the number of instant and sorcery
///    cards in your graveyard."
///
/// ## Implementation
///
/// Two independent pieces, both expressible with existing engine primitives —
/// no new infrastructure. Shape mirrors <see cref="CracklingDrakeFactory"/>,
/// differing in three ways:
///   (a) no ETB draw trigger — Enigma Drake has none;
///   (b) the CDA scans the controller's <em>graveyard only</em> (Crackling
///       Drake also scans exile);
///   (c) mana cost {1}{U}{R} and no color indicator.
///
/// 1. <b>Flying (CR 702.9)</b> — a <see cref="KeywordAbility"/> marker, read by
///    the combat block-restriction path.
///
/// 2. <b>Layer 7a characteristic-defining power (CR 604.3 / 613.2)</b> — the
///    printed power is "*", defined by a CDA. Implemented via
///    <see cref="CdaPowerToughnessEffect"/> whose power evaluator returns the
///    count of instant + sorcery cards the controller <em>owns</em> in their
///    own <em>graveyard</em>. Toughness is NOT characteristic-defining (printed
///    4); the CDA's toughness evaluator returns the fixed printed 4 so Layer 7a
///    leaves it at 4 and later 7c counters/anthems stack on top.
///
///    "in your graveyard": cards in a player's graveyard are always owned by
///    that player (CR 404.2 — cards go to their owner's graveyard); the helper
///    additionally filters by <c>Owner == controller</c> as belt-and-braces
///    (CR 109.5 — "you" / "your" refer to the controller).
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so the
/// CDA registers/unregisters as the Drake enters/leaves the battlefield. The
/// single-arg <see cref="Create(Player)"/> overload produces a card with
/// correct identity and Flying but no live CDA — suitable for pure card-shape
/// tests.
/// </summary>
[CardName("Enigma Drake")]
public static class EnigmaDrakeFactory
{
    public const string CardName = "Enigma Drake";
    public const string PrintedManaCost = "{1}{U}{R}";

    /// <summary>Printed toughness — fixed, not characteristic-defining.</summary>
    public const int PrintedToughness = 4;

    /// <summary>
    /// Creates an Enigma Drake with correct identity and Flying, but no live
    /// Layer 7a CDA. Suitable for shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Enigma Drake. When <paramref name="effects"/> is
    /// supplied, a <see cref="CdaPowerToughnessEffect"/> is attached so the
    /// Layer 7a power CDA registers/unregisters as the Drake enters/leaves the
    /// battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="effects"/> is null the
    /// lifecycle wiring is skipped (matches the shape-only overload).
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the CDA
    /// against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be null —
    /// the CDA's <see cref="CdaPowerToughnessEffect.IsActive"/> battlefield
    /// gate covers correctness, but no explicit unregister will fire.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed P/T = */4. Seed BasePower=0 (Layer 7a overwrites it on every
        // Compute); BaseToughness=4 is the real printed toughness.
        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 0,
            toughness: PrintedToughness,
            subtypes: new[] { CardSubtype.Drake });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Block restrictions enforced by
        // CombatRules / CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ---------------------------------------------------------------
        // Layer 7a CDA power lifecycle wiring.
        // ---------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new EnigmaDrakeCdaLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Count instant + sorcery cards in <paramref name="cards"/> owned by
    /// <paramref name="owner"/> (CR 109.5 — "you" = controller; "your
    /// graveyard" restricts to the controller's cards). Pure helper exposed for
    /// tests; mirrors the closure baked into the live
    /// <see cref="CdaPowerToughnessEffect"/>.
    /// </summary>
    public static int CountInstantsAndSorceries(IEnumerable<ICard> cards, Player owner)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(owner);

        var count = 0;
        foreach (var card in cards)
        {
            if (!ReferenceEquals(card.Owner, owner)) continue;
            if (card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery)) count++;
        }
        return count;
    }

    /// <summary>
    /// Compute Enigma Drake's CDA power for the supplied controller — the
    /// number of instant/sorcery cards the controller owns in their own
    /// graveyard. Read fresh on every Layer 7a Compute (CR 613.2).
    /// </summary>
    private static int ComputePower(Player controller)
        => CountInstantsAndSorceries(controller.Zones.Graveyard.GetCards(), controller);

    /// <summary>
    /// ETB/LTB lifecycle binder for Enigma Drake's power CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when the Drake enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="CracklingDrakeFactory"/>'s lifecycle binder; the toughness
    /// evaluator returns the fixed printed 4 (only power is CDA-defined).
    /// </summary>
    private sealed class EnigmaDrakeCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public EnigmaDrakeCdaLifecycle(
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
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    // CR 109.5 — "your graveyard" reads against the live controller.
                    powerOf: src => ComputePower(src.Controller ?? _source.Controller!),
                    // Toughness is the fixed printed 4 (not characteristic-
                    // defining); 7c counters/anthems stack on top.
                    toughnessOf: _ => PrintedToughness);
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
