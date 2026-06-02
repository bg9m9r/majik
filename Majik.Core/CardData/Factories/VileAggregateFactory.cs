using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vile Aggregate (Battle for Zendikar, {2}{R}).
///
/// Creature — Eldrazi Drone (colorless — Devoid), printed P/T */5. Oracle
/// text (verified against Scryfall):
///   "Devoid (This card has no color.)
///    Vile Aggregate's power is equal to the number of colorless creatures
///    you control.
///    Trample
///    Ingest (Whenever this creature deals combat damage to a player, that
///    player exiles the top card of their library.)"
///
/// ## Shape source
/// Card identity (name, Creature — Eldrazi Drone, {2}{R}, 0/5 seed) is loaded
/// from <c>Majik.Core/CardData/Cards/vile-aggregate.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. Devoid, the Layer-7a power CDA,
/// Trample, and the Ingest combat-damage trigger are layered on in code — the
/// JSON <c>AbilityDefinition</c> schema doesn't express Devoid, CDAs, Trample,
/// or combat-damage triggers (same posture as
/// <see cref="NettleDroneFactory"/> / <see cref="WrithingChrysalisFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Devoid (CR 702.114)</b> — stamped via <see cref="Card.SetDevoid"/> so
///   <see cref="CardColors.GetColors"/> returns empty regardless of the {R}
///   pip, plus a <see cref="KeywordAbility"/> marker. Same shape as
///   <see cref="NettleDroneFactory"/>. Note Vile Aggregate is itself
///   colorless, so it counts toward its own power.
/// - <b>Power CDA (CR 604.3 / 613.2)</b> — "Vile Aggregate's power is equal to
///   the number of colorless creatures you control." Modeled as a Layer 7a
///   <see cref="CdaPowerToughnessEffect"/> whose power evaluator counts the
///   colorless creatures the controller controls (CR 105.2c — a colorless
///   object has no color; <see cref="CardColors.GetColors"/> returns an empty
///   set). Its toughness evaluator returns the printed 5 — only power is
///   characteristic-defined, so the 7a write restores the printed toughness
///   each Compute while Layer 7c pumps / counters (CR 613.7) stack on top.
///   The controlled-creatures snapshot is supplied as a
///   <see cref="Func{TResult}"/> closure read fresh on every Compute (same
///   live-read posture as <see cref="TarmogoyfFactory"/>'s graveyard source).
///   ETB/LTB lifecycle mirrors <see cref="TarmogoyfFactory"/> /
///   <see cref="DeathsShadowFactory"/>.
/// - <b>Trample (CR 702.19)</b> — <see cref="KeywordAbility"/> marker read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> for the
///   excess-combat-damage rule. Same posture as
///   <see cref="AkoumWarriorFactory"/>.
/// - <b>Ingest (CR 702.115 / CR 510, CR 603.1)</b> — "Whenever this creature
///   deals combat damage to a player, that player exiles the top card of their
///   library." A <see cref="TriggeredAbility"/> over
///   <see cref="CombatDamageDealtEvent"/> filtered to this card as source and a
///   non-null <see cref="DamageDealtEvent.TargetPlayer"/>; resolution exiles
///   the top card of the damaged player's library (empty-library is a no-op —
///   CR 120.3, the loss-condition is an SBA, not this effect). Same
///   combat-damage-to-a-player shape as <see cref="RagavanNimblePilfererFactory"/>
///   minus the Treasure / may-cast grant.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (Devoid + Trample + Ingest
///   trigger attached, no live CDA, trigger unregistered). Suitable for
///   shape / dispatcher tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?, TriggerManager?, ZoneService?, Func{IEnumerable{ICard}})"/>
///   — fully wired: the power CDA registers/unregisters on ETB/LTB and the
///   Ingest trigger registers so a combat-damage event queues it.
/// </summary>
[CardName("Vile Aggregate")]
public static class VileAggregateFactory
{
    public const string CardName = "Vile Aggregate";
    public const string Slug = "vile-aggregate";
    public const int PrintedToughness = 5;

    public const string DevoidKeyword = "Devoid";
    public const string TrampleKeyword = "Trample";
    public const string IngestKeyword = "Ingest";

    /// <summary>
    /// Construct Vile Aggregate with no live wiring. Devoid + Trample + the
    /// Ingest trigger are attached structurally; the trigger is NOT registered
    /// and no live power CDA is attached (power reads its printed seed 0). This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null, triggers: null, zoneService: null, controlledCreaturesSource: null);

    /// <summary>
    /// Construct a fully-wired Vile Aggregate.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the power
    /// CDA against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for the CDA's ETB/LTB lifecycle.</param>
    /// <param name="triggers">Trigger manager — when supplied the Ingest
    /// trigger registers so a <see cref="CombatDamageDealtEvent"/> queues it
    /// (CR 603.3).</param>
    /// <param name="zoneService">When supplied the Ingest exile move publishes
    /// a <see cref="CardMovedEvent"/>.</param>
    /// <param name="controlledCreaturesSource">Closure returning every creature
    /// the controller controls (typically
    /// <c>() =&gt; owner.Zones.Battlefield.GetCards()</c>). Read fresh on every
    /// Compute. Pass null for shape-only (the CDA is then skipped).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService,
        Func<IEnumerable<ICard>>? controlledCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi + Drone subtypes, {2}{R}, 0/5 seed — the power CDA overwrites
        // the 0 when active; the 5 is the printed toughness).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors returns
        // empty despite the {R} pip; plus a keyword marker for ability scans.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // CR 702.19 — Trample. Keyword marker read by CombatAbilities.HasTrample
        // for the excess-combat-damage assignment rule.
        card.AddAbility(new KeywordAbility(TrampleKeyword, card, owner));

        // ----------------------------------------------------------------
        // Power CDA — CR 604.3 / 613.2 (Layer 7a).
        //   "Vile Aggregate's power is equal to the number of colorless
        //    creatures you control."
        // Power = count of colorless creatures the controller controls
        // (CR 105.2c — colorless == empty color set). Toughness restores the
        // printed 5 (only power is characteristic-defined). Registered on ETB,
        // unregistered on LTB; same lifecycle as Tarmogoyf / Death's Shadow.
        // ----------------------------------------------------------------
        if (effects != null && controlledCreaturesSource != null)
        {
            var lifecycle = new VileAggregateCdaLifecycle(
                card, effects, eventBus, controlledCreaturesSource);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // Ingest — CR 702.115 / CR 510 / CR 603.1.
        //   "Whenever this creature deals combat damage to a player, that
        //    player exiles the top card of their library."
        // The predicate captures the damaged player off the event; the effect
        // exiles the top of that player's library (empty-library no-op —
        // CR 120.3). Same combat-damage-to-a-player shape as Ragavan minus the
        // Treasure / may-cast grant.
        // ----------------------------------------------------------------
        Player? capturedDamaged = null;

        var ingestEffect = new Effect(
            $"{CardName}: damaged player exiles the top card of their library (Ingest)",
            () =>
            {
                var victim = capturedDamaged;
                if (victim == null) return;

                var top = victim.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — SBAs own the loss

                if (zoneService != null)
                {
                    zoneService.MoveCard(top, ZoneType.Library, ZoneType.Exile, victim);
                }
                else
                {
                    victim.Zones.Library.RemoveCard(top);
                    victim.Zones.Exile.AddCard(top);
                    top.SetZone(ZoneType.Exile);
                }
            });

        var ingestTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false;
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { ingestEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ingestTrigger);
        triggers?.RegisterTriggeredAbility(ingestTrigger);

        return card;
    }

    /// <summary>
    /// Pure helper: count the colorless creatures among the supplied controlled
    /// permanents (CR 105.2c — colorless == empty color set). Exposed for tests;
    /// mirrors the closure baked into the live power CDA.
    /// </summary>
    public static int CountColorlessCreatures(IEnumerable<ICard> controlled)
    {
        ArgumentNullException.ThrowIfNull(controlled);
        var count = 0;
        foreach (var c in controlled)
        {
            if (!c.HasType(CardType.Creature)) continue;
            if (CardColors.GetColors(c).Count == 0) count++;
        }
        return count;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Vile Aggregate's power CDA. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Vile Aggregate enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="TarmogoyfFactory"/>'s lifecycle binder.
    /// </summary>
    private sealed class VileAggregateCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _controlledSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public VileAggregateCdaLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<IEnumerable<ICard>> controlledSource)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _controlledSource = controlledSource;
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
                    powerOf: _ => CountColorlessCreatures(_controlledSource()),
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
