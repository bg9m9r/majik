using System;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fracture (Strixhaven, {W}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target artifact, enchantment, or planeswalker."
///
/// ## Declarative spell schema (destroy_target)
/// Card shape is loaded from the embedded JSON definition
/// (<c>fracture.json</c>) via <see cref="CardDefinitionLoader.FromEmbeddedResource"/>
/// and materialized through <see cref="CardDefinitionFactory"/>.
/// <see cref="BuildDefinition"/> declares a single
/// <see cref="DestroyTargetEffectDef"/> verb on the new
/// <c>"artifact_enchantment_or_planeswalker"</c> target filter and routes it
/// through <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the
/// same ability-side <c>destroy_target</c> verb Disenchant / Naturalize /
/// Boseiju use, only with planeswalker added to the eligible-type predicate.
/// The "artifact, enchantment, or planeswalker" restriction is enforced both
/// at gather time and (CR 608.2b) at resolution via the shared
/// <see cref="TargetFilters"/> predicate, so an off-type raw target fizzles
/// cleanly. Indestructible (CR 702.12) / regeneration (CR 701.15) are honoured
/// by the Destroy-reason gate in
/// <see cref="Majik.Core.Primitives.Fx.MoveToGraveyard(ICard, Majik.Core.Zones.ZoneMoveReason)"/>.
/// </summary>
[CardName("Fracture")]
public static class FractureFactory
{
    public const string CardName = "Fracture";
    public const string Slug = "fracture";

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the "destroy target artifact, enchantment, or planeswalker"
    /// <see cref="SpellDefinition"/> declaratively (the <c>destroy_target</c>
    /// verb on the <c>artifact_enchantment_or_planeswalker</c> target filter).
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
                new DestroyTargetEffectDef { TargetFilter = "artifact_enchantment_or_planeswalker" },
            });
}
