using System;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smelt (Born of the Gods, {R}).
///
/// Instant. Oracle text:
///   "Destroy target artifact."
///
/// ## Declarative spell schema (destroy_target)
/// <see cref="BuildDefinition"/> declares a single
/// <see cref="DestroyTargetEffectDef"/> verb (filter <c>"artifact"</c>) and routes
/// it through <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the
/// same ability-side <c>destroy_target</c> verb Shatter / Boseiju use. The
/// "artifact only" restriction is enforced both at gather time and (CR 608.2b) at
/// resolution via the shared <see cref="TargetFilters"/> predicate. Indestructible
/// (CR 702.12) / regeneration (CR 701.15) are honoured by the Destroy-reason gate
/// in <see cref="Majik.Core.Primitives.Fx.MoveToGraveyard(ICard, Majik.Core.Zones.ZoneMoveReason)"/>.
/// </summary>
[CardName("Smelt")]
public static class SmeltFactory
{
    public const string CardName = "Smelt";
    public const string PrintedManaCost = "{R}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target artifact) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target artifact" <see cref="SpellDefinition"/>
    /// declaratively (the <c>destroy_target</c> verb on the <c>artifact</c>
    /// target filter).
    /// </summary>
    /// <param name="targetResolver">Accepted for call-site compatibility with
    /// the bespoke spell factories; the declarative path reads the cast flow's
    /// already-resolved target directly, so the resolver is effectively
    /// identity.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object>? targetResolver = null) =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new DestroyTargetEffectDef { TargetFilter = "artifact" },
            });
}
