using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lumra, Bellow of the Woods (Bloomburrow,
/// {4}{G}{G}). Legendary Creature — Elemental Bear, printed P/T */*.
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Vigilance, reach
///    Lumra's power and toughness are each equal to the number of lands you
///    control.
///    When Lumra enters, mill four cards. Then return all land cards from
///    your graveyard to the battlefield tapped."
///
/// The base shape (name, Legendary supertype, Creature, Elemental + Bear
/// subtypes, {4}{G}{G}, printed 0/0 placeholder for the */* CDA) is
/// materialised from the embedded JSON definition
/// (<c>lumra-bellow-of-the-woods.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Vigilance, Reach, the
/// characteristic-defining P/T, and the ETB trigger are layered on here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express keyword markers, CDA
/// P/T, or ETB triggers (same posture as
/// <see cref="AdelineResplendentCatharFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Vigilance (CR 702.21) + Reach (CR 702.17)</b> — two
///   <see cref="KeywordAbility"/> markers so <c>ICard.Abilities</c> reflects
///   the printed line and Scryfall keyword parsing matches. Combat reads them
///   off the keyword set.
///
/// - <b>"Lumra's power and toughness are each equal to the number of lands you
///   control" (CR 604.3 / 613.2 Layer 7a)</b> — a characteristic-defining
///   ability implemented via <see cref="CdaPowerToughnessEffect"/> whose power
///   AND toughness evaluators each count every <see cref="CardType.Land"/> on
///   the battlefield under Lumra's controller (read fresh on every Compute, so
///   it tracks lands entering / leaving live — same evaluator-closure posture
///   as <see cref="AdelineResplendentCatharFactory"/>). Layer 7a SETS P/T; 7c
///   counters / anthems stack on top (CR 613.7). The CDA registers when Lumra
///   enters the battlefield and unregisters when she leaves, via a
///   <see cref="CardMovedEvent"/>-driven lifecycle mirroring Adeline's. Printed
///   P/T is seeded 0/0 (CR 208.2c — "*" is treated as the CDA-defined value;
///   Layer 7a overwrites the seed).
///
/// - <b>"When Lumra enters, mill four cards. Then return all land cards from
///   your graveyard to the battlefield tapped." (CR 603.6a)</b> — a
///   <see cref="TriggeredAbility"/> gated on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution it first
///   mills four (<see cref="Fx.Mill"/>, CR 701.13 — top four of the
///   controller's library to their graveyard), THEN snapshots every land card
///   in the controller's graveyard and returns each to the battlefield via
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> (ZoneService-routed so
///   ETB triggers fire, CR 603.6a) and taps it (CR 701 — "tapped"). The mill
///   happens BEFORE the return, so lands milled by this very trigger are
///   included in the return set (the printed "Then" sequencing — CR 608.2).
///   The live <see cref="ZoneService"/> is fetched from
///   <see cref="ZoneServiceRegistry"/> at resolution so the prod-routed build
///   reanimates through the real zone service without a captured dependency.
///
/// The single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity + keyword markers + the ETB trigger shape but no live
/// CDA / TriggerManager registration — suitable for card-shape / dispatcher
/// tests.
/// </summary>
[CardName("Lumra, Bellow of the Woods")]
public static class LumraBellowOfTheWoodsFactory
{
    public const string CardName = "Lumra, Bellow of the Woods";
    public const string Slug = "lumra-bellow-of-the-woods";

    /// <summary>Granted keyword — CR 702.21 Vigilance.</summary>
    public const string Vigilance = "Vigilance";

    /// <summary>Granted keyword — CR 702.17 Reach.</summary>
    public const string Reach = "Reach";

    /// <summary>Cards milled by the ETB trigger (CR 701.13).</summary>
    public const int MillCount = 4;

    /// <summary>
    /// Construct Lumra with no live runtime wiring (the dispatcher / shape
    /// path). Vigilance, Reach, and the ETB trigger are attached for shape
    /// observability; the CDA is not registered (no effects service) and the
    /// ETB trigger is not registered with a <see cref="TriggerManager"/>. This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, landsYouControlSource: null,
            triggers: null);

    /// <summary>
    /// Construct Lumra with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the CDA P/T
    /// (<see cref="CdaPowerToughnessEffect"/>) registers against. May be null —
    /// the CDA is then not wired and P/T falls back to the printed 0/0
    /// seed.</param>
    /// <param name="eventBus">Event bus for the CDA's ETB/LTB lifecycle
    /// (<see cref="CardMovedEvent"/>). May be null — the CDA's battlefield gate
    /// still covers correctness, but no explicit unregister fires.</param>
    /// <param name="landsYouControlSource">Closure returning the cards to count
    /// for "lands you control" — typically
    /// <c>() =&gt; controller.Zones.Battlefield.GetCards()</c>. The CDA filters
    /// to <see cref="CardType.Land"/>. Read fresh on every Compute. May be null
    /// (CDA not wired).</param>
    /// <param name="triggers">TriggerManager the ETB trigger is registered with
    /// so a <see cref="CardMovedEvent"/> for Lumra entering the battlefield
    /// lands it on the stack (CR 603.3). May be null.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? landsYouControlSource,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Elemental + Bear, {4}{G}{G}; printed P/T seeded 0/0). No
        // abilities in the JSON — all of them layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.21 / CR 702.17 — Vigilance + Reach keyword markers.
        card.AddAbility(new KeywordAbility(Vigilance, card, owner));
        card.AddAbility(new KeywordAbility(Reach, card, owner));

        // CR 604.3 / 613.2 Layer 7a — "Lumra's power and toughness are each
        // equal to the number of lands you control."
        if (effects != null && landsYouControlSource != null)
        {
            var lifecycle = new LumraCdaLifecycle(card, effects, eventBus, landsYouControlSource);
            lifecycle.Attach();
        }

        // CR 603.6a — "When Lumra enters, mill four cards. Then return all land
        // cards from your graveyard to the battlefield tapped."
        var etbEffect = new Effect(
            $"{CardName}: mill {MillCount}, then return all land cards from your graveyard to the battlefield tapped",
            ctx =>
            {
                ResolveEtb(card, owner);
                return ValueTask.CompletedTask;
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Count "lands you control" among the supplied cards. Pure helper exposed
    /// for tests; mirrors the closure baked into the live CDA.
    /// </summary>
    public static int CountLands(IEnumerable<ICard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.Count(c => c.HasType(CardType.Land));
    }

    /// <summary>
    /// Resolve the ETB trigger: mill four, then return every land card from the
    /// controller's graveyard to the battlefield tapped (CR 603.6a / 701.13).
    /// Exposed for tests; mirrors the closure baked into the live trigger.
    /// </summary>
    public static void ResolveEtb(Creature card, Player owner)
    {
        var controller = card.Controller ?? owner;

        // CR 701.13 — mill four (top four of controller's library to graveyard).
        Fx.Mill(controller, MillCount);

        // "Then return all land cards from your graveyard to the battlefield
        // tapped." Snapshot the land cards up front (the move mutates the
        // graveyard in place). The mill already ran, so freshly-milled lands are
        // part of this set (CR 608.2 — resolved as printed, in order).
        var zones = ZoneServiceRegistry.Get(controller);
        var lands = controller.Zones.Graveyard.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();

        foreach (var land in lands)
        {
            Fx.ReturnFromGraveyardToBattlefield(land, controller, zones);
            // CR 701 — the returned permanents enter tapped.
            if (land is Permanent perm && perm.Zone == ZoneType.Battlefield)
            {
                perm.Tap();
            }
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Lumra's CDA P/T. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Lumra enters the battlefield,
    /// unregisters when she leaves. Mirrors Adeline's lifecycle.
    /// </summary>
    private sealed class LumraCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _landsSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public LumraCdaLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<IEnumerable<ICard>> landsSource)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _landsSource = landsSource;
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
                    // CR 604.3 — "number of lands you control"; power AND
                    // toughness each take this value (CR 208.2c both are "*").
                    powerOf: _ => CountLands(_landsSource()),
                    toughnessOf: _ => CountLands(_landsSource()));
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
