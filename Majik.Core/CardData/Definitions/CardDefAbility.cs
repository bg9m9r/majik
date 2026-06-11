using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// PLAN 01 (Slice F) — pairs an effect-builder with the optional
/// <see cref="TargetRequest"/> the effect targets through. The builder
/// receives the index of this effect's request within the owning ability's
/// declared <c>TargetRequests</c> (<c>-1</c> for an untargeted effect), so at
/// resolution it reads its chosen target from the matching
/// <see cref="ResolutionContext.ChosenTargets"/> slot. This is how a JSON /
/// DSL ability declares its targets and routes them through the shared
/// <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline — the same as
/// a hand-written factory.
/// </summary>
/// <para>
/// The <see cref="Build"/> closure additionally receives the live per-game
/// <see cref="ContinuousEffectsService"/> (or <c>null</c> on the pure-shape
/// test path) so a verb that registers a CR 613 continuous effect — currently
/// <see cref="GainControlEffectDef"/> (the Threaten / Zealous Conscripts "gain
/// control until end of turn" family, which installs a
/// <see cref="Majik.Core.Effects.TemporaryControlChangeEffect"/> + an until-EOT
/// haste grant) — can reach it at materialization time on the ABILITY path,
/// mirroring how <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>
/// threads it on the SPELL path. Verbs that don't need it ignore the extra
/// argument, so the produced effect is byte-identical for them.
/// </para>
/// <para>
/// <see cref="SharesPreviousTargetSlot"/> marks a <b>rider</b> effect that
/// reuses the immediately-preceding targeted effect's chosen target instead of
/// declaring its own slot — the canonical case being the "its controller loses
/// N life" half of a Vapor-Snag-style bounce (<see
/// cref="LoseLifeTargetEffectDef"/> in <c>Subject="controller"</c> mode). The
/// flag mirrors <see cref="EffectDefinition.SharesPreviousTargetSlot"/>; when
/// set, the spec contributes NO <see cref="TargetRequest"/> (so
/// <see cref="Request"/> must be <c>null</c>) and
/// <see cref="CardDefAbilityEffects.Materialize"/> hands the
/// <see cref="Build"/> closure the <c>targetRequestIndex</c> of the most-recent
/// targeted effect — exactly as
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> does on the
/// SPELL path — so the rider reads the SHARED pick at resolution and fizzles
/// with its host (CR 608.2b) when that target is illegal.
/// </para>
internal sealed record CardDefEffectSpec(
    TargetRequest? Request,
    Func<ICard, Player, ReplacementBus?, int, ContinuousEffectsService?, IEffect> Build,
    IReadOnlyList<TargetRequest>? ExtraRequests = null,
    bool SharesPreviousTargetSlot = false);

/// <summary>
/// Canonical, runtime-agnostic representation of a card ability carried on
/// <see cref="CardDef.Abilities"/>. This is the convergence point for the two
/// declarative card systems (PLAN 03): the JSON
/// <see cref="CardDefinition"/> ability union (mana / activated / triggered)
/// is mapped onto this shape by <see cref="CardDefinition.ToCardDef"/> (via
/// the <c>ToCost()</c> / <c>ToResolveEffect()</c> / <c>ToTrigger()</c>
/// mappers on the JSON union types), and <see cref="CardDefRuntime.Build"/>
/// is the <b>one</b> interpreter that materializes it into a live
/// <see cref="IAbility"/>.
///
/// <para>
/// Each ability stores its cost / effect / trigger pieces as deferred
/// builders that close over the live <see cref="ICard"/> /
/// <see cref="Player"/> at <see cref="CardDefRuntime.Build"/> time. The
/// builders delegate to the shared
/// <see cref="Majik.Core.Primitives.Costs"/> /
/// <see cref="Majik.Core.Primitives.Fx"/> /
/// <see cref="Majik.Core.Abilities.Triggers"/> vocabulary so the runtime
/// cards are byte-identical to the ones the legacy direct
/// <c>CardDefinitionFactory</c> path built before the reroute (PLAN 03 S2 is
/// behaviour-neutral).
/// </para>
/// </summary>
public abstract class CardDefAbility
{
    /// <summary>Materialize this ability for the live card + controller.
    /// <paramref name="continuous"/> is the live per-game continuous-effects
    /// service threaded to verbs that register a CR 613 continuous effect
    /// (currently <c>gain_control</c>); <c>null</c> on the pure-shape path.</summary>
    internal abstract IAbility Build(
        ICard card, Player controller, ReplacementBus? replacements,
        ContinuousEffectsService? continuous = null);
}

/// <summary>
/// "{T}: Add &lt;produces&gt;" (optionally with an extra mana cost) mana
/// ability. Mirrors <see cref="ManaAbilityDefinition"/>; the builder closure
/// is supplied by the JSON mapper so the additional-cost / vanilla split
/// stays in one place.
/// </summary>
public sealed class CardDefManaAbility : CardDefAbility
{
    private readonly Func<ICard, Player, ManaAbility> _builder;

    internal CardDefManaAbility(Func<ICard, Player, ManaAbility> builder) => _builder = builder;

    internal override IAbility Build(
        ICard card, Player controller, ReplacementBus? replacements,
        ContinuousEffectsService? continuous = null) =>
        _builder(card, controller);
}

/// <summary>
/// Non-mana activated ability — pay all <see cref="CostBuilders"/>, then run
/// every <see cref="EffectBuilders"/> in printed order. CR 117.1a / 307.5 —
/// <see cref="SorcerySpeed"/> threads the "activate only as a sorcery" rider
/// onto the runtime <see cref="ActivatedAbility"/>.
/// </summary>
public sealed class CardDefActivatedAbility : CardDefAbility
{
    internal IReadOnlyList<Func<ICard, ICost>> CostBuilders { get; }
    internal IReadOnlyList<CardDefEffectSpec> EffectSpecs { get; }
    internal bool SorcerySpeed { get; }

    internal CardDefActivatedAbility(
        IReadOnlyList<Func<ICard, ICost>> costBuilders,
        IReadOnlyList<CardDefEffectSpec> effectSpecs,
        bool sorcerySpeed)
    {
        CostBuilders = costBuilders;
        EffectSpecs = effectSpecs;
        SorcerySpeed = sorcerySpeed;
    }

    internal override IAbility Build(
        ICard card, Player controller, ReplacementBus? replacements,
        ContinuousEffectsService? continuous = null)
    {
        var costs = CostBuilders.Select(b => b(card)).ToArray();
        var (effects, requests) = CardDefAbilityEffects.Materialize(
            EffectSpecs, card, controller, replacements, continuous);
        // PLAN 01 (Slice F) — declare the ability's target requests so
        // AbilityActivationFlow.ActivateAsync collects them via the shared
        // TargetCollection pipeline (CR 602.2b) and stamps ChosenTargets that
        // the effects read at resolution.
        return new ActivatedAbility(
            source: card,
            controller: controller,
            costs: costs,
            effects: effects,
            targetRequests: requests,
            sorcerySpeed: SorcerySpeed,
            // STAGE 2/3 (re-sourceable abilities) — every CardDef verb reads its
            // source/subject off the live ResolutionContext: self-source verbs
            // (pump / connive / explore) were migrated to ResolutionContext.Source;
            // the rest are scoped to the controller or to ChosenTargets. So the
            // whole data-driven activated ability is sound to re-home via
            // ActivatedAbility.RebindTo — Agatha's Soul Cauldron grants the REAL
            // ability of an imprinted creature this way rather than re-parsing
            // its oracle text.
            rebindSafe: true);
    }
}

/// <summary>
/// Triggered ability — <see cref="TriggerBuilder"/> picks the condition;
/// <see cref="EffectBuilders"/> resolve in order when it fires.
/// </summary>
public sealed class CardDefTriggeredAbility : CardDefAbility
{
    internal Func<ICard, ITriggerCondition> TriggerBuilder { get; }
    internal IReadOnlyList<CardDefEffectSpec> EffectSpecs { get; }

    /// <summary>
    /// The zones in which the built <see cref="TriggeredAbility"/> stays active
    /// (<see cref="TriggeredAbility.ActiveZones"/>). <c>null</c> means "use the
    /// engine default" (battlefield only). A leaves-the-battlefield trigger
    /// (e.g. <c>dies_self</c>) supplies the Graveyard here so it remains
    /// observable after the zone stamp (CR 603.6d / CR 700.4).
    /// </summary>
    internal IReadOnlyList<Majik.Core.Zones.ZoneType>? ActiveZones { get; }

    /// <summary>
    /// CR 601.2b / 603.4 — the generalized optional reflexive "you may pay
    /// {cost}. If you do, …" mana rider on the WHOLE ability. <c>null</c> = the
    /// effect list runs unconditionally. When set, the materialized effect array
    /// is wrapped in a single gating effect (<see cref="OptionalManaRider"/>)
    /// that prompts the controller's agent yes/no, pays the cost, and only then
    /// runs the gated effects in order. Eldrazi Obligator is the canonical case.
    /// </summary>
    internal Majik.Core.ValueObjects.ManaCost? OptionalManaCost { get; }

    internal CardDefTriggeredAbility(
        Func<ICard, ITriggerCondition> triggerBuilder,
        IReadOnlyList<CardDefEffectSpec> effectSpecs,
        IReadOnlyList<Majik.Core.Zones.ZoneType>? activeZones = null,
        Majik.Core.ValueObjects.ManaCost? optionalManaCost = null)
    {
        TriggerBuilder = triggerBuilder;
        EffectSpecs = effectSpecs;
        ActiveZones = activeZones;
        OptionalManaCost = optionalManaCost;
    }

    internal override IAbility Build(
        ICard card, Player controller, ReplacementBus? replacements,
        ContinuousEffectsService? continuous = null)
    {
        var condition = TriggerBuilder(card);
        var (effects, requests) = CardDefAbilityEffects.Materialize(
            EffectSpecs, card, controller, replacements, continuous);
        // CR 601.2b / 603.4 — gate the whole effect list behind the optional
        // reflexive payment when present. Target requests are returned unwrapped
        // so the engine still collects targets as the trigger goes on the stack
        // (CR 603.3d), independent of the later payment.
        var resolveEffects = OptionalManaCost is { } cost
            ? new IEffect[] { OptionalManaRider.Wrap(card, controller, cost, effects) }
            : effects;
        return new TriggeredAbility(
            source: card,
            controller: controller,
            condition: condition,
            effects: resolveEffects,
            targetRequests: requests,
            activeZones: ActiveZones);
    }
}

/// <summary>
/// PLAN 01 (Slice F) — shared materializer that turns an ability's
/// <see cref="CardDefEffectSpec"/> list into the parallel runtime
/// <see cref="IEffect"/> array plus the ordered <see cref="TargetRequest"/>
/// list. A targeting effect's request is appended in declaration order, and
/// the effect builder is handed that request's index so at resolution it
/// reads the matching <see cref="ResolutionContext.ChosenTargets"/> slot.
/// Untargeted effects get index <c>-1</c> and contribute no request.
/// </summary>
internal static class CardDefAbilityEffects
{
    internal static (IEffect[] Effects, IReadOnlyList<TargetRequest> Requests) Materialize(
        IReadOnlyList<CardDefEffectSpec> specs,
        ICard card,
        Player controller,
        ReplacementBus? replacements,
        ContinuousEffectsService? continuous = null)
    {
        var effects = new IEffect[specs.Count];
        var requests = new List<TargetRequest>();
        // The slot index of the most-recently declared targeted effect, so a
        // rider spec (SharesPreviousTargetSlot — e.g. Vapor Snag's "its
        // controller loses N life" on an activated/triggered ability) reuses it
        // instead of declaring a fresh slot. -1 until the first targeted effect
        // appears. Mirrors CardDefRuntime.BuildSpellDefinitionFromEffects on
        // the SPELL path so the rider behaves identically on both paths.
        var lastTargetedSlot = -1;
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var index = -1;
            if (spec.Request is not null)
            {
                index = requests.Count;
                lastTargetedSlot = requests.Count;
                requests.Add(spec.Request);
                // CR 701.12 fight (source: "target") — the verb declares one or
                // more ADDITIONAL contiguous target slots (the "other" creature,
                // and N-slot verbs more) right after its primary (the fighter).
                // It reads its picks at index, index+1, … index+N at resolution.
                if (spec.ExtraRequests is { Count: > 0 } extras)
                {
                    requests.AddRange(extras);
                }
            }
            else if (spec.SharesPreviousTargetSlot)
            {
                // Rider — reuse the preceding targeted effect's slot (no new
                // TargetRequest). Falls back to untargeted (-1) if it is the
                // first effect, so the rider's resolution-time target read sees
                // no pick and fizzles cleanly (CR 608.2b).
                index = lastTargetedSlot;
            }
            effects[i] = spec.Build(card, controller, replacements, index, continuous);
        }
        return (effects, requests);
    }
}
