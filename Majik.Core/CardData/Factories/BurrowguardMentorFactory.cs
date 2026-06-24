using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Burrowguard Mentor (Bloomburrow, {G}{W}).
///
/// Creature — Rabbit Soldier, printed power "*" / toughness "*". Oracle text
/// (verified against Scryfall 2026-06-23):
///   "Trample
///    Burrowguard Mentor's power and toughness are each equal to the number of
///    creatures you control."
///
/// ## Shape source
/// Card identity (name, {G}{W}, Creature — Rabbit Soldier, green + white, and
/// the Trample keyword marker) is materialised from the embedded JSON
/// definition (<c>burrowguard-mentor.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same data-driven posture as
/// <see cref="ConiferWurmFactory"/> (Trample-from-JSON) and
/// <see cref="AdelineResplendentCatharFactory"/> (creatures-you-control CDA).
/// The JSON <c>keywords</c> line becomes a plain <see cref="KeywordAbility"/>
/// marker automatically (CardDefRuntime keyword path). The characteristic-
/// defining P/T is layered on here — the JSON <c>AbilityDefinition</c> schema
/// does not express CDA P/T.
///
/// ## Implemented (v1)
/// - <b>Trample (CR 702.19)</b> — a <see cref="KeywordAbility"/> marker emitted
///   from the JSON keyword line; CombatDamage reads it for excess-damage
///   trampling.
/// - <b>"power and toughness are each equal to the number of creatures you
///   control" (CR 604.3 / 613.2 Layer 7a)</b> — a characteristic-defining
///   ability implemented via <see cref="CdaPowerToughnessEffect"/> whose power
///   AND toughness evaluators each count every <see cref="CardType.Creature"/>
///   on the battlefield under Burrowguard Mentor's controller (read fresh on
///   every Compute, so it tracks creatures entering / leaving live — same
///   evaluator-closure posture as <see cref="AdelineResplendentCatharFactory"/>
///   and <see cref="MortivoreFactory"/>). Burrowguard Mentor counts itself
///   among "creatures you control" (it is a creature on the battlefield), so its
///   minimum P/T on the battlefield is 1/1. The CDA registers when it enters
///   the battlefield and unregisters when it leaves, via a
///   <see cref="CardMovedEvent"/>-driven lifecycle mirroring Adeline's. Printed
///   P/T is seeded 0/0 (CR 208.2c — "*" is treated as the CDA-defined value;
///   Layer 7a overwrites the seed).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trample marker attached (from
///   JSON); the CDA is not registered (no effects service) so P/T falls back to
///   the printed 0/0 seed. This is the overload <see cref="NamedCardFactory"/>
///   dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?, Func{IEnumerable{ICard}}?)"/>
///   — fully wired; the CDA registers/unregisters via <see cref="CardMovedEvent"/>.
/// </summary>
[CardName("Burrowguard Mentor")]
public static class BurrowguardMentorFactory
{
    public const string CardName = "Burrowguard Mentor";
    public const string Slug = "burrowguard-mentor";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Burrowguard Mentor with no live runtime wiring. The Trample
    /// marker is attached (from the JSON keyword line); the CDA is not registered
    /// (no effects service) so P/T falls back to the printed 0/0 seed. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, creaturesYouControlSource: null);

    /// <summary>
    /// Construct Burrowguard Mentor with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the CDA P/T
    /// (<see cref="CdaPowerToughnessEffect"/>) registers against. May be null —
    /// the CDA is then not wired and P/T falls back to the printed 0/0 seed.</param>
    /// <param name="eventBus">Event bus for the CDA's ETB/LTB lifecycle
    /// (<see cref="CardMovedEvent"/>). May be null — the CDA's battlefield gate
    /// still covers correctness, but no explicit unregister fires.</param>
    /// <param name="creaturesYouControlSource">Closure returning the cards to
    /// count for "creatures you control" — typically
    /// <c>() =&gt; controller.Zones.Battlefield.GetCards()</c>. The CDA filters
    /// to <see cref="CardType.Creature"/>. Read fresh on every Compute. May be
    /// null (CDA not wired).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? creaturesYouControlSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: Creature — Rabbit Soldier,
        // {G}{W}, green + white, Trample keyword marker; printed P/T seeded 0/0
        // (CR 208.2c — "*" treated as the CDA-defined value, Layer 7a overwrites).
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 604.3 / 613.2 Layer 7a — "power and toughness are each equal to the
        // number of creatures you control." Both evaluators count the same value.
        if (effects != null && creaturesYouControlSource != null)
        {
            var lifecycle = new BurrowguardCdaLifecycle(card, effects, eventBus, creaturesYouControlSource);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Count "creatures you control" among the supplied cards. Pure helper
    /// exposed for tests; mirrors the closure baked into the live CDA.
    /// </summary>
    public static int CountCreatures(IEnumerable<ICard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.Count(c => c.HasType(CardType.Creature));
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Burrowguard Mentor's CDA P/T. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when it enters the battlefield,
    /// unregisters when it leaves. Mirrors Adeline's lifecycle — only the
    /// toughness evaluator differs (here it also tracks the creature count rather
    /// than a fixed printed value).
    /// </summary>
    private sealed class BurrowguardCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _creaturesSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public BurrowguardCdaLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<IEnumerable<ICard>> creaturesSource)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _creaturesSource = creaturesSource;
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
                    // CR 604.3 — both power AND toughness = "number of creatures
                    // you control" (read fresh each Compute).
                    powerOf: _ => CountCreatures(_creaturesSource()),
                    toughnessOf: _ => CountCreatures(_creaturesSource()));
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
