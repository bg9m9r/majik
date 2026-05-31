using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Definitions;

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
    internal IReadOnlyList<Func<ICard, Player, ReplacementBus?, IEffect>> EffectBuilders { get; }
    internal bool SorcerySpeed { get; }

    internal CardDefActivatedAbility(
        IReadOnlyList<Func<ICard, ICost>> costBuilders,
        IReadOnlyList<Func<ICard, Player, ReplacementBus?, IEffect>> effectBuilders,
        bool sorcerySpeed)
    {
        CostBuilders = costBuilders;
        EffectBuilders = effectBuilders;
        SorcerySpeed = sorcerySpeed;
    }

    internal override IAbility Build(ICard card, Player controller, ReplacementBus? replacements)
    {
        var costs = CostBuilders.Select(b => b(card)).ToArray();
        var effects = EffectBuilders.Select(b => b(card, controller, replacements)).ToArray();
        return new ActivatedAbility(
            source: card,
            controller: controller,
            costs: costs,
            effects: effects,
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
    internal IReadOnlyList<Func<ICard, Player, ReplacementBus?, IEffect>> EffectBuilders { get; }

    internal CardDefTriggeredAbility(
        Func<ICard, ITriggerCondition> triggerBuilder,
        IReadOnlyList<Func<ICard, Player, ReplacementBus?, IEffect>> effectBuilders)
    {
        TriggerBuilder = triggerBuilder;
        EffectBuilders = effectBuilders;
    }

    internal override IAbility Build(ICard card, Player controller, ReplacementBus? replacements)
    {
        var condition = TriggerBuilder(card);
        var effects = EffectBuilders.Select(b => b(card, controller, replacements)).ToArray();
        return new TriggeredAbility(
            source: card,
            controller: controller,
            condition: condition,
            effects: effects);
    }
}
