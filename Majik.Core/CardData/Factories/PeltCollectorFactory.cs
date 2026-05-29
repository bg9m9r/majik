using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pelt Collector (Guilds of Ravnica, {G}).
///
/// Creature — Elf Warrior 1/1. Oracle text (verified against Scryfall):
///   "Whenever another creature you control enters or dies, if that
///    creature's power is greater than this creature's, put a +1/+1
///    counter on this creature.
///    As long as this creature has three or more +1/+1 counters on it,
///    it has trample."
///
/// The base shape (name, Creature, Elf Warrior subtypes, {G}, 1/1) is
/// materialised from the embedded JSON definition
/// (<c>pelt-collector.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (the power-gated enter-or-dies counter trigger and the counter-
/// threshold-gated Trample static) are layered on top here — the JSON
/// <see cref="AbilityDefinition"/> schema expresses neither an
/// enter-or-dies trigger with an own-power comparison nor a counter-count-
/// gated keyword grant, so they live in the factory (same posture as
/// <see cref="StormscaleScionFactory"/> / <see cref="HangarbackWalkerFactory"/>
/// whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
///
/// - 1/1 <see cref="Creature"/> — Elf Warrior, {G}, owner/controller wired.
/// - <b>Power-gated enter-or-dies counter trigger (CR 603.1 / CR 603.4 /
///   CR 603.6e)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="CardMovedEvent"/> that fires when ANOTHER creature this
///   card's controller controls either enters the battlefield OR dies
///   (Battlefield → Graveyard). The printed "if that creature's power is
///   greater than this creature's" is an intervening-if condition — the
///   trigger only fires (and only stays on the stack) while the qualifying
///   creature's power strictly exceeds Pelt Collector's current power. The
///   power comparison is captured against the specific event's creature so
///   the intervening-if (CR 603.4) re-checks the SAME creature at
///   resolution. On resolve, places one +1/+1 counter on Pelt Collector
///   via <see cref="CountersService.Add"/> (so Hardened Scales /
///   Doubling Season can rewrite the amount per CR 614, and the
///   post-commit <see cref="CounterAddedEvent"/> fires for counters-matter
///   payoffs).
///   <para>Both halves of "enters or dies" are one triggered ability with
///   a single firing per qualifying event (CR 603.1) — the entering case
///   reads the entering creature's live power; the dying case reads its
///   last-known power (CR 608.2g — the creature has moved to the
///   graveyard, but its <see cref="Creature.Power"/> getter still returns
///   its last-known value because the trigger condition is evaluated at
///   the moment the move event fires).</para>
/// - <b>Counter-threshold Trample static (CR 613.1f / CR 702.19)</b>:
///   "As long as this creature has three or more +1/+1 counters on it, it
///   has trample." Wired as a <see cref="PeltCollectorTrampleEffect"/>
///   Layer-6 (Abilities) continuous effect that grants the Trample keyword
///   to Pelt Collector itself ONLY while it has &gt;= 3 +1/+1 counters and
///   is on the battlefield. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied; the gate is a
///   live read of the counter bag each layer pass, so the keyword appears
///   the moment the third counter lands and lifts if counters are removed
///   (CR 121.4 / CR 122.6). <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>
///   surfaces it through the layer system's computed keyword set.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The enter-or-dies trigger
///   is attached for shape observability but not registered with any
///   <see cref="TriggerManager"/>; the Trample static is NOT registered
///   (no continuous-effects service). Counter placement uses the direct
///   <see cref="CountersService.Add"/> fallthrough (no replacement rewrites,
///   no event publish). This is the overload <see cref="NamedCardFactory"/>
///   dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, IEventBus?, ContinuousEffectsService?)"/>
///   — fully wired. The enter-or-dies trigger registers; counter placement
///   routes through the replacement bus + publishes
///   <see cref="CounterAddedEvent"/>; the Trample static registers with the
///   layer system.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Last-known-information for the dying creature's controller</b>:
///   CR 603.10 — "you control" should be read from LKI at the moment of
///   death. v1 reads <see cref="ICard.Controller"/> off the moved card
///   directly (it is still its last controller right after the zone-move
///   event). Same posture as <see cref="FalkenrathNobleFactory"/> /
///   Blood Artist.
/// </summary>
[CardName("Pelt Collector")]
public static class PeltCollectorFactory
{
    public const string CardName = "Pelt Collector";
    public const string Slug = "pelt-collector";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>CR 122 / CR 702.19 — the +1/+1 counter threshold at which
    /// Pelt Collector gains Trample.</summary>
    public const int TrampleCounterThreshold = 3;

    /// <summary>
    /// Construct Pelt Collector with no live wiring. The enter-or-dies
    /// trigger is attached structurally (it fires correctly when its
    /// effects are executed directly); it is NOT registered with any
    /// <see cref="TriggerManager"/>, and the Trample static is NOT
    /// registered (no continuous-effects service). Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, eventBus: null, continuousEffects: null);

    /// <summary>
    /// Construct Pelt Collector with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager. When supplied the
    /// enter-or-dies counter trigger registers so a qualifying
    /// <see cref="CardMovedEvent"/> automatically queues the ability
    /// (CR 603.2).</param>
    /// <param name="replacements">ReplacementBus. When supplied the +1/+1
    /// counter placement routes through <see cref="CountersService.Add"/>
    /// so Hardened Scales / Doubling Season can rewrite the count
    /// (CR 614).</param>
    /// <param name="eventBus">EventBus. When supplied the counter
    /// placement publishes <see cref="CounterAddedEvent"/> so
    /// counters-matter payoffs (Animation Module, Conclave Mentor) can
    /// chain.</param>
    /// <param name="continuousEffects">Layers service the counter-gated
    /// Trample static registers against. Pass null to skip the static
    /// (shape-only).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf Warrior, {G}, 1/1). The JSON carries no abilities — the
        // power-gated trigger + Trample static are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Power-gated enter-or-dies counter trigger — CR 603.1 / 603.4.
        //   "Whenever another creature you control enters or dies, if that
        //    creature's power is greater than this creature's, put a +1/+1
        //    counter on this creature."
        //
        // Single TriggeredAbility covering both the "enters" and "dies"
        // halves via one CardMovedEvent predicate. The power comparison is
        // an intervening-if (CR 603.4): checked when the trigger would fire
        // AND re-checked on resolution. We capture the specific qualifying
        // creature off the event so the intervening-if re-reads the SAME
        // creature's power at resolution time.
        // ----------------------------------------------------------------
        Creature? pendingCreature = null;

        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            // "another creature" — must be a different creature object.
            if (e.Card is not Creature other) return false;
            if (ReferenceEquals(other, card)) return false;

            // "you control" — the moved creature is controlled by Pelt
            // Collector's controller. CR 603.10 / 608.2g — for the dies
            // half this is the last-known controller, still stamped on the
            // card object immediately after the zone-move event.
            if (!ReferenceEquals(other.Controller, card.Controller)) return false;

            // "enters or dies":
            //   enters → ToZone == Battlefield
            //   dies   → Battlefield → Graveyard (CR 700.4)
            var enters = e.ToZone == ZoneType.Battlefield;
            var dies = e.FromZone == ZoneType.Battlefield && e.ToZone == ZoneType.Graveyard;
            if (!enters && !dies) return false;

            // "if that creature's power is greater than this creature's"
            // (CR 603.4 — the intervening-if checked at trigger time). The
            // dying creature's Power getter returns its last-known power
            // because the condition runs at the moment the move event fires.
            if (other.Power <= card.Power) return false;

            // Capture for the resolution-time intervening-if re-check.
            pendingCreature = other;
            return true;
        });

        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on self",
            () =>
            {
                // CR 614 — route through CountersService.Add so Hardened
                // Scales / Doubling Season rewrite the count and the
                // post-commit CounterAddedEvent fires.
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements, eventBus);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { counterEffect },
            // CR 603.4 — intervening-if re-checked at resolution. The
            // counter is placed only if the captured creature's power still
            // strictly exceeds Pelt Collector's. (Returns false if no
            // creature was captured, which cannot happen on a real fire.)
            interveningIf: () => pendingCreature is { } pc && pc.Power > card.Power,
            // The dies half fires after the ZoneService has stamped the
            // dying creature's Zone = Graveyard; the trigger's own source
            // (Pelt Collector) is on the battlefield, so Battlefield is the
            // only active zone the ability needs.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // ----------------------------------------------------------------
        // Counter-threshold Trample static — CR 613.1f / CR 702.19.
        //   "As long as this creature has three or more +1/+1 counters on
        //    it, it has trample."
        // Layer-6 continuous effect gated on the live counter count; only
        // registered when a layers service is available.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new PeltCollectorTrampleEffect(card));
        }

        return card;
    }
}

/// <summary>
/// CR 613.1f (Layer 6 — ability-adding) / CR 702.19 — grants the Trample
/// keyword to its <see cref="Creature"/> source while that source is on
/// the battlefield AND has three or more +1/+1 counters on it
/// (<see cref="PeltCollectorFactory.TrampleCounterThreshold"/>). The
/// counter count is read live each layer pass, so the keyword appears the
/// moment the third counter lands and lifts if the count drops below the
/// threshold (CR 121.4 / CR 122.6).
/// </summary>
public sealed class PeltCollectorTrampleEffect : ContinuousEffect
{
    private readonly Creature _source;

    public PeltCollectorTrampleEffect(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    /// <summary>Active only while the source is on the battlefield AND has
    /// reached the +1/+1 counter threshold (CR 122.6 / 702.19).</summary>
    public override bool IsActive() =>
        _source.Zone == ZoneType.Battlefield
        && _source.Counters.Count(CounterType.PlusOnePlusOne)
            >= PeltCollectorFactory.TrampleCounterThreshold;

    /// <summary>The static grants Trample to its own source only.</summary>
    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars) =>
        chars.Keywords.Add("Trample");
}
