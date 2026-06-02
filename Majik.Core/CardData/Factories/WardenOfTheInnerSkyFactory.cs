using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Warden of the Inner Sky (Murders at Karlov Manor,
/// {W}).
///
/// Creature — Human Soldier 1/2. Oracle text (verified against Scryfall):
///   "As long as this creature has three or more counters on it, it has flying
///    and vigilance.
///    Tap three untapped artifacts and/or creatures you control: Put a +1/+1
///    counter on this creature. Scry 1. Activate only as a sorcery."
///
/// The base shape (name, Creature, Human Soldier subtypes, {W}, 1/2) is
/// materialised from the embedded JSON definition
/// (<c>warden-of-the-inner-sky.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (the counter-threshold-gated Flying + Vigilance static and the tap-three /
/// counter+scry sorcery-speed activated ability) are layered on top here — the
/// JSON <see cref="AbilityDefinition"/> schema expresses neither a
/// counter-count-gated multi-keyword grant nor a printed-word tap-as-cost, so
/// they live in the factory (same posture as <see cref="PeltCollectorFactory"/>
/// whose counter-threshold keyword static this cribs directly).
///
/// ## Implemented (v1)
///
/// - 1/2 <see cref="Creature"/> — Human Soldier, {W}, owner/controller wired.
/// - <b>Counter-threshold Flying + Vigilance static (CR 613.1f / CR 702.9 /
///   CR 702.20)</b>: "As long as this creature has three or more counters on
///   it, it has flying and vigilance." Wired as a
///   <see cref="WardenOfTheInnerSkyFlyingVigilanceEffect"/> Layer-6
///   (Abilities) continuous effect that grants BOTH keywords to Warden itself
///   ONLY while it has &gt;= 3 counters of ANY kind on it and is on the
///   battlefield. The oracle text says "three or more counters" (not
///   specifically +1/+1), so the gate sums every counter type
///   (<see cref="CounterCollection.All"/>), matching CR 122.1 (a counter is a
///   counter regardless of kind). Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied; the gate is a live
///   read of the counter bag each layer pass, so the keywords appear the
///   moment the third counter lands and lift if counters are removed
///   (CR 121.4 / CR 122.6).
/// - <b>Tap-three: +1/+1 counter + Scry 1, sorcery speed (CR 602.1 /
///   CR 602.5d / CR 701.20)</b>: a <see cref="ActivatedAbility"/> with a single
///   <see cref="TapUntappedArtifactsOrCreaturesCost"/> for three permanents and
///   no target. <see cref="ActivatedAbility.IsSorcerySpeed"/> is true — the
///   "Activate only as a sorcery" rider (CR 117.1a / CR 307.5). Resolution
///   places one +1/+1 counter on Warden via <see cref="CountersService.Add"/>
///   (so Hardened Scales / Doubling Season can rewrite the amount per CR 614,
///   and the post-commit <see cref="CounterAddedEvent"/> fires for
///   counters-matter payoffs — and crucially advances the static's threshold),
///   then runs the standard <see cref="ScryAction"/> pipeline (N=1),
///   agent-driven when an agent is registered, all-bottom default otherwise.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The Flying/Vigilance static is
///   NOT registered (no continuous-effects service). The activated ability's
///   counter placement uses the direct <see cref="CountersService.Add"/>
///   fallthrough (no replacement rewrites, no event publish). This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ReplacementBus?, IEventBus?, ContinuousEffectsService?)"/>
///   — fully wired. The Flying/Vigilance static registers with the layer
///   system; the activated ability's counter placement routes through the
///   replacement bus + publishes <see cref="CounterAddedEvent"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent prompt for which three permanents to tap</b>: the cost falls back
///   to the first three eligible (untapped, controller-owned artifacts and/or
///   creatures) in battlefield order via
///   <see cref="TapUntappedArtifactsOrCreaturesCost"/>'s deterministic pick.
///   Agents may pre-set <see cref="TapUntappedArtifactsOrCreaturesCost.Targets"/>
///   to override.
/// </summary>
[CardName("Warden of the Inner Sky")]
public static class WardenOfTheInnerSkyFactory
{
    public const string CardName = "Warden of the Inner Sky";
    public const string Slug = "warden-of-the-inner-sky";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>Number of artifacts and/or creatures tapped to activate.</summary>
    public const int PermanentsToTap = 3;

    /// <summary>Scry amount on the activated ability's resolution.</summary>
    public const int ScryAmount = 1;

    /// <summary>CR 122.1 — the counter threshold at which Warden gains Flying
    /// and Vigilance.</summary>
    public const int KeywordCounterThreshold = 3;

    /// <summary>
    /// Construct Warden of the Inner Sky with no live wiring. The
    /// Flying/Vigilance static is NOT registered (no continuous-effects
    /// service); the tap-three activated ability is attached structurally (it
    /// resolves correctly when its effects are executed directly). Suitable for
    /// shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, eventBus: null, continuousEffects: null);

    /// <summary>
    /// Construct Warden of the Inner Sky with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">ReplacementBus. When supplied the +1/+1
    /// counter placement routes through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season can rewrite the count (CR 614).</param>
    /// <param name="eventBus">EventBus. When supplied the counter placement
    /// publishes <see cref="CounterAddedEvent"/> so counters-matter payoffs can
    /// chain.</param>
    /// <param name="continuousEffects">Layers service the counter-gated
    /// Flying/Vigilance static registers against. Pass null to skip the static
    /// (shape-only).</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human
        // Soldier, {W}, 1/2). The JSON carries no abilities — the
        // counter-threshold static + tap-three activation are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Tap three untapped artifacts and/or creatures you control:
        //   Put a +1/+1 counter on this creature. Scry 1.
        //   Activate only as a sorcery.
        // CR 602.1 — activated ability with a printed-word tap-as-cost
        // (CR 118.12) for three artifacts/creatures and a sorcery-speed rider
        // (CR 117.1a / 307.5). No target. Resolution places a +1/+1 counter
        // (CR 614 — via CountersService.Add) then scries 1 (CR 701.20).
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on self, then scry {ScryAmount}",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 614 — route through CountersService.Add so Hardened Scales
                // / Doubling Season rewrite the count, the post-commit
                // CounterAddedEvent fires, and the Flying/Vigilance static's
                // live threshold read picks up the new total.
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements, eventBus);

                // CR 701.20 — scry 1. Standard ScryAction pipeline, agent-driven
                // when an agent is registered, all-bottom default otherwise
                // (same body as Stormwing Entity's ETB scry).
                var peeked = ScryAction.Peek(controller, ScryAmount);
                if (peeked.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Pre-agent default: all peeked cards to bottom.
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }

                ScryAction.Apply(controller, peeked.Count, decision);
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new TapUntappedArtifactsOrCreaturesCost(PermanentsToTap),
            },
            effects: new IEffect[] { activatedEffect },
            // CR 117.1a / 307.5 — "Activate only as a sorcery."
            sorcerySpeed: true);

        card.AddAbility(activated);

        // ----------------------------------------------------------------
        // Counter-threshold Flying + Vigilance static — CR 613.1f / 702.9 /
        // 702.20.
        //   "As long as this creature has three or more counters on it, it has
        //    flying and vigilance."
        // Layer-6 continuous effect gated on the live total counter count
        // (any kind, CR 122.1); only registered when a layers service is
        // available.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new WardenOfTheInnerSkyFlyingVigilanceEffect(card));
        }

        return card;
    }
}

/// <summary>
/// CR 613.1f (Layer 6 — ability-adding) / CR 702.9 (Flying) / CR 702.20
/// (Vigilance) — grants the Flying AND Vigilance keywords to its
/// <see cref="Creature"/> source while that source is on the battlefield AND
/// has three or more counters of ANY kind on it
/// (<see cref="WardenOfTheInnerSkyFactory.KeywordCounterThreshold"/>). The
/// total counter count is read live each layer pass (summing every counter
/// type per CR 122.1), so the keywords appear the moment the third counter
/// lands and lift if the count drops below the threshold (CR 121.4 /
/// CR 122.6).
/// </summary>
public sealed class WardenOfTheInnerSkyFlyingVigilanceEffect : ContinuousEffect
{
    private readonly Creature _source;

    public WardenOfTheInnerSkyFlyingVigilanceEffect(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.Abilities;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    /// <summary>Active only while the source is on the battlefield AND has
    /// reached the total-counter threshold (CR 122.1 / 122.6 / 702.9 /
    /// 702.20).</summary>
    public override bool IsActive() =>
        _source.Zone == ZoneType.Battlefield
        && _source.Counters.All.Values.Sum()
            >= WardenOfTheInnerSkyFactory.KeywordCounterThreshold;

    /// <summary>The static grants the keywords to its own source only.</summary>
    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add("Flying");
        chars.Keywords.Add("Vigilance");
    }
}
