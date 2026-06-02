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
/// Named-card factory for Voice of the Blessed (Innistrad: Midnight Hunt,
/// {W}{W}).
///
/// Creature — Spirit Cleric 2/2. Oracle text (verified against the embedded
/// Scryfall-sourced seed):
///   "Whenever you gain life, put a +1/+1 counter on this creature.
///    As long as this creature has four or more +1/+1 counters on it, it has
///    flying and vigilance.
///    As long as this creature has ten or more +1/+1 counters on it, it has
///    indestructible."
///
/// The base shape (name, Creature, Spirit Cleric subtypes, {W}{W}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>voice-of-the-blessed.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (the lifegain +1/+1 trigger and the two counter-threshold-gated keyword
/// statics) are layered on top here — the JSON <see cref="AbilityDefinition"/>
/// schema expresses neither a lifegain trigger nor a counter-count-gated
/// keyword grant, so they live in the factory (same posture as
/// <see cref="AjaniPridemateFactory"/> for the trigger and
/// <see cref="WardenOfTheInnerSkyFactory"/> for the counter-gated keyword
/// statics, both of which this cribs directly).
///
/// ## Implemented (v1)
///
/// - 2/2 <see cref="Creature"/> — Spirit Cleric, {W}{W}, owner/controller wired.
/// - <b>Lifegain trigger (CR 603.6a / CR 119.3 / CR 122.1)</b>: "Whenever you
///   gain life, put a +1/+1 counter on this creature." Wired via
///   <see cref="Triggers.OnLifeGainedByPlayer"/> consuming
///   <see cref="LifeChangedEvent"/> (filtered to the controller AND strictly
///   positive deltas — life *gain*, not loss). One
///   <see cref="CounterType.PlusOnePlusOne"/> counter is placed per resolution
///   regardless of the gained amount, routed through
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
///   style replacements (CR 614) can rewrite the count and the post-commit
///   <see cref="CounterAddedEvent"/> advances the keyword-static thresholds.
/// - <b>Flying + Vigilance at four +1/+1 counters (CR 613.1f / 702.9 /
///   702.20)</b>: a Layer-6 (Abilities) continuous effect granting BOTH
///   keywords to Voice itself only while it is on the battlefield AND has four
///   or more +1/+1 counters on it. The oracle text reads "four or more +1/+1
///   counters" specifically, so the gate counts only
///   <see cref="CounterType.PlusOnePlusOne"/> (NOT every counter kind — this is
///   where it diverges from Warden of the Inner Sky's "three or more counters").
/// - <b>Indestructible at ten +1/+1 counters (CR 613.1f / 702.12)</b>: a second
///   Layer-6 continuous effect granting Indestructible to Voice itself only
///   while on the battlefield AND with ten or more +1/+1 counters. Same
///   live-count gate on <see cref="CounterType.PlusOnePlusOne"/>.
/// - Both statics are live reads of the +1/+1 counter count each layer pass, so
///   each keyword set appears the moment its threshold is reached and lifts if
///   the count drops back below it (CR 121.4 / CR 122.6). Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The lifegain trigger is attached
///   structurally (no <see cref="TriggerManager"/> registration); the keyword
///   statics are NOT registered (no continuous-effects service). This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, IEventBus?, ContinuousEffectsService?)"/>
///   — fully wired. The trigger registers so a qualifying
///   <see cref="LifeChangedEvent"/> auto-queues the ability; the keyword statics
///   register with the layer system; counter placement routes through the
///   replacement bus + publishes <see cref="CounterAddedEvent"/>.
/// </summary>
[CardName("Voice of the Blessed")]
public static class VoiceOfTheBlessedFactory
{
    public const string CardName = "Voice of the Blessed";
    public const string Slug = "voice-of-the-blessed";

    /// <summary>CR 122.1 — +1/+1 counter threshold for Flying + Vigilance.</summary>
    public const int FlyingVigilanceCounterThreshold = 4;

    /// <summary>CR 122.1 — +1/+1 counter threshold for Indestructible.</summary>
    public const int IndestructibleCounterThreshold = 10;

    /// <summary>
    /// Construct Voice of the Blessed with no live wiring. The lifegain trigger
    /// is attached to the card shape (it resolves correctly when its effects are
    /// executed directly); the counter-threshold keyword statics are NOT
    /// registered (no continuous-effects service). Suitable for shape / dispatch
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, eventBus: null, continuousEffects: null);

    /// <summary>
    /// Construct Voice of the Blessed with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager. When supplied the lifegain trigger
    /// is registered so a qualifying <see cref="LifeChangedEvent"/> auto-queues
    /// the ability.</param>
    /// <param name="replacements">ReplacementBus. When supplied the +1/+1
    /// counter placement routes through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season can rewrite the count (CR 614).</param>
    /// <param name="eventBus">EventBus. When supplied the counter placement
    /// publishes <see cref="CounterAddedEvent"/> so counters-matter payoffs can
    /// chain.</param>
    /// <param name="continuousEffects">Layers service the counter-gated keyword
    /// statics register against. Pass null to skip them (shape-only).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Spirit
        // Cleric, {W}{W}, 2/2). The JSON carries no abilities — the lifegain
        // trigger + two counter-threshold keyword statics are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Lifegain trigger — CR 603.6a / CR 119.3 / CR 122.1.
        //   "Whenever you gain life, put a +1/+1 counter on this creature."
        //
        // Condition: LifeChangedEvent for the controller, strict
        // NewLife > PreviousLife (Triggers.OnLifeGainedByPlayer encodes both
        // filters). One counter regardless of gained amount.
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it (controller gained life)",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements, eventBus));

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByPlayer(owner),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        // ----------------------------------------------------------------
        // Counter-threshold keyword statics — CR 613.1f / 702.9 / 702.20 /
        // 702.12. Layer-6 continuous effects gated on the live +1/+1 counter
        // count; only registered when a layers service is available.
        //   - "four or more +1/+1 counters" → Flying + Vigilance
        //   - "ten or more +1/+1 counters" → Indestructible
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new VoiceOfTheBlessedFlyingVigilanceEffect(card));
            continuousEffects.Register(new VoiceOfTheBlessedIndestructibleEffect(card));
        }

        return card;
    }

    /// <summary>CR 122.1 — live count of +1/+1 counters on <paramref name="c"/>.
    /// The oracle text gates specifically on "+1/+1 counters" (not any counter
    /// kind), so only that type is counted.</summary>
    internal static int PlusOnePlusOneCount(Creature c) =>
        c.Counters.Count(CounterType.PlusOnePlusOne);
}

/// <summary>
/// CR 613.1f (Layer 6 — ability-adding) / CR 702.9 (Flying) / CR 702.20
/// (Vigilance) — grants Flying AND Vigilance to its <see cref="Creature"/>
/// source while that source is on the battlefield AND has four or more +1/+1
/// counters on it
/// (<see cref="VoiceOfTheBlessedFactory.FlyingVigilanceCounterThreshold"/>). The
/// +1/+1 counter count is read live each layer pass, so the keywords appear the
/// moment the fourth +1/+1 counter lands and lift if the count drops below the
/// threshold (CR 121.4 / CR 122.6).
/// </summary>
public sealed class VoiceOfTheBlessedFlyingVigilanceEffect : ContinuousEffect
{
    private readonly Creature _source;

    public VoiceOfTheBlessedFlyingVigilanceEffect(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    /// <summary>Active only while the source is on the battlefield AND has four
    /// or more +1/+1 counters on it (CR 122.1 / 122.6 / 702.9 / 702.20).</summary>
    public override bool IsActive() =>
        _source.Zone == ZoneType.Battlefield
        && VoiceOfTheBlessedFactory.PlusOnePlusOneCount(_source)
            >= VoiceOfTheBlessedFactory.FlyingVigilanceCounterThreshold;

    /// <summary>The static grants the keywords to its own source only.</summary>
    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add("Flying");
        chars.Keywords.Add("Vigilance");
    }
}

/// <summary>
/// CR 613.1f (Layer 6 — ability-adding) / CR 702.12 (Indestructible) — grants
/// Indestructible to its <see cref="Creature"/> source while that source is on
/// the battlefield AND has ten or more +1/+1 counters on it
/// (<see cref="VoiceOfTheBlessedFactory.IndestructibleCounterThreshold"/>). The
/// +1/+1 counter count is read live each layer pass, so the keyword appears the
/// moment the tenth +1/+1 counter lands and lifts if the count drops below the
/// threshold (CR 121.4 / CR 122.6).
/// </summary>
public sealed class VoiceOfTheBlessedIndestructibleEffect : ContinuousEffect
{
    private readonly Creature _source;

    public VoiceOfTheBlessedIndestructibleEffect(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    /// <summary>Active only while the source is on the battlefield AND has ten or
    /// more +1/+1 counters on it (CR 122.1 / 122.6 / 702.12).</summary>
    public override bool IsActive() =>
        _source.Zone == ZoneType.Battlefield
        && VoiceOfTheBlessedFactory.PlusOnePlusOneCount(_source)
            >= VoiceOfTheBlessedFactory.IndestructibleCounterThreshold;

    /// <summary>The static grants the keyword to its own source only.</summary>
    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add("Indestructible");
    }
}
