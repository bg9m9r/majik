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
internal sealed record CardDefEffectSpec(
    TargetRequest? Request,
    Func<ICard, Player, ReplacementBus?, int, IEffect> Build);

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
    /// <summary>Materialize this ability for the live card + controller.</summary>
    internal abstract IAbility Build(ICard card, Player controller, ReplacementBus? replacements);
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

    internal override IAbility Build(ICard card, Player controller, ReplacementBus? replacements) =>
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

    internal override IAbility Build(ICard card, Player controller, ReplacementBus? replacements)
    {
        var costs = CostBuilders.Select(b => b(card)).ToArray();
        var (effects, requests) = CardDefAbilityEffects.Materialize(EffectSpecs, card, controller, replacements);
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
            sorcerySpeed: SorcerySpeed);
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

    internal CardDefTriggeredAbility(
        Func<ICard, ITriggerCondition> triggerBuilder,
        IReadOnlyList<CardDefEffectSpec> effectSpecs)
    {
        TriggerBuilder = triggerBuilder;
        EffectSpecs = effectSpecs;
    }

    internal override IAbility Build(ICard card, Player controller, ReplacementBus? replacements)
    {
        var condition = TriggerBuilder(card);
        var (effects, requests) = CardDefAbilityEffects.Materialize(EffectSpecs, card, controller, replacements);
        return new TriggeredAbility(
            source: card,
            controller: controller,
            condition: condition,
            effects: effects,
            targetRequests: requests);
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
        ReplacementBus? replacements)
    {
        var effects = new IEffect[specs.Count];
        var requests = new List<TargetRequest>();
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var index = -1;
            if (spec.Request is not null)
            {
                index = requests.Count;
                requests.Add(spec.Request);
            }
            effects[i] = spec.Build(card, controller, replacements, index);
        }
        return (effects, requests);
    }
}
