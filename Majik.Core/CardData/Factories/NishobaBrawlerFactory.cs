using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using DomainRule = Majik.Core.Rules.Domain;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nishoba Brawler (Time Spiral, {1}{G}).
///
/// Creature — Cat Warrior, printed P/T <c>*/3</c>. Oracle text:
///   "Trample
///    Domain — Nishoba Brawler's power is equal to the number of basic
///    land types among lands you control."
///
/// ## Implementation
///
/// Card shape (type / subtypes / mana cost / toughness) is authored in
/// <c>nishoba-brawler.json</c> and materialized via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/>. The
/// two behavioural pieces — Trample and the Domain power CDA — are not
/// expressible in the data schema yet, so they are wired in C# on top of
/// the built card (same posture as <see cref="FangrenHunterFactory"/> for
/// Trample and <see cref="TarmogoyfFactory"/> for the CDA).
///
/// ### Trample (CR 702.19)
/// Attached as a <see cref="KeywordAbility"/> marker, read by
/// <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> in the
/// combat damage-assignment path.
///
/// ### Domain power CDA (CR 604.3 / 613.2 / 702.16)
/// "Nishoba Brawler's power is equal to the number of basic land types
/// among lands you control" is a <b>characteristic-defining ability</b>
/// that SETS power in Layer 7a — modelled with
/// <see cref="CdaPowerToughnessEffect"/>. The power evaluator counts the
/// controller's distinct basic land types via
/// <see cref="DomainRule.CountTypes(Player, ContinuousEffectsService?)"/>
/// (CR 702.16 — the five basic land types {Plains, Island, Swamp,
/// Mountain, Forest}; a single dual/triome contributes every basic type
/// it has, duplicates collapse). Toughness is a normal printed <c>3</c>
/// — NOT characteristic-defining — so the toughness evaluator simply
/// returns the printed value; Layer 7c pumps / counters then stack on
/// top of both (CR 613.7).
///
/// The CDA is re-evaluated on every
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>, so the
/// power tracks live land state with no caching. Lifecycle (register on
/// ETB / unregister on LTB) mirrors <see cref="TarmogoyfFactory"/>:
/// subscribe to <see cref="CardMovedEvent"/> on the supplied
/// <see cref="IEventBus"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Layer 4 feed-through in the count</b>: the CDA passes
///   <c>effects = null</c> to <see cref="DomainRule.CountTypes"/> to avoid
///   re-entrant Compute recursion, so it uses printed land subtypes. Blood
///   Moon / Spreading Seas / Urborg / Yavimaya retypes don't feed through
///   the live count until the engine has a two-pass dependency resolver —
///   same posture as <see cref="TerritorialKavuFactory"/>'s Domain pump.
/// </summary>
[CardName("Nishoba Brawler")]
public static class NishobaBrawlerFactory
{
    public const string CardName = "Nishoba Brawler";
    public const string Slug = "nishoba-brawler";

    /// <summary>
    /// Creates a Nishoba Brawler with correct card identity + Trample, but
    /// no live Layer 7a CDA. Suitable for factory-shape / dispatch tests
    /// and the <see cref="NamedCardFactory"/> dispatcher path.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Nishoba Brawler. When <paramref name="effects"/>
    /// is supplied a <see cref="CdaPowerToughnessEffect"/> is attached so
    /// the Layer 7a Domain power CDA registers/unregisters as the card
    /// enters/leaves the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="effects"/> is null
    /// the lifecycle wiring is skipped (matches the shape-only overload).
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the
    /// CDA against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be
    /// null — the CDA's <see cref="CdaPowerToughnessEffect.IsActive"/> gate
    /// covers correctness, but no explicit unregister will fire.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Card shape from the embedded JSON definition: Cat Warrior,
        // {1}{G}, printed toughness 3. Printed power 0 is a harmless seed —
        // the Layer 7a CDA overwrites it on every Compute (CR 208.2c).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.19 — Trample. CombatAbilities.HasTrample reads the marker.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        if (effects != null)
        {
            var lifecycle = new DomainPowerCdaLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Nishoba Brawler's Domain power CDA.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when the card enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="TarmogoyfFactory"/>'s lifecycle binder.
    /// </summary>
    private sealed class DomainPowerCdaLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<GameEvent> _handler;
        private readonly int _printedToughness;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public DomainPowerCdaLifecycle(
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
            // Toughness is printed (not characteristic-defining); snapshot
            // the base so the 7a evaluator can re-assert it without pulling
            // in 7c pumps (those stack on top per CR 613.7).
            _printedToughness = source.BaseToughness;
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
                    // CR 702.16 — power = distinct basic land types the
                    // controller controls. effects=null avoids re-entrant
                    // Compute (printed-subtypes mode), matching
                    // TerritorialKavuFactory's posture.
                    powerOf: _ => DomainRule.CountTypes(_controller, effects: null),
                    // Printed toughness (3) — not a CDA value.
                    toughnessOf: _ => _printedToughness);
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
