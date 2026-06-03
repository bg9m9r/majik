using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ancient Grudge (Time Spiral, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target artifact.
///    Flashback {G} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>ancient-grudge.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> (same data-only shape as
/// <see cref="PlayWithFireFactory"/>). The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
///
/// - <b>Destroy target artifact</b> — <see cref="BuildDefinition"/> routes a
///   single <see cref="DestroyTargetEffectDef"/> verb (filter <c>"artifact"</c>)
///   through <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the
///   same declarative <c>destroy_target</c> verb Shatter / Smelt / Boseiju use.
///   The "artifact only" restriction is enforced both at gather time AND
///   (CR 608.2b) at resolution via the shared <see cref="TargetFilters"/>
///   predicate, so a target whose type changed before resolution fizzles
///   cleanly. <c>Fx.MoveToGraveyard(…, Destroy)</c> (CR 701.7) honours
///   indestructible (CR 702.12) / regeneration (CR 701.15) shields.
/// - <b>Printed Flashback {G}</b> (CR 702.34) alt-cost: produced via
///   <see cref="GetFlashbackAlternativeCost"/> so callers (bots / integration
///   tests) can cast Ancient Grudge from the graveyard via
///   <see cref="FlashbackAlternativeCost"/>. Post-resolve exile (CR 702.34b)
///   is handled by <see cref="FlashbackAlternativeCost.OnResolved"/> — same
///   alt-cost wiring as every other printed-flashback card
///   (<see cref="PastInFlamesFactory"/> / <see cref="FaithlessLootingFactory"/>).
/// </summary>
[CardName("Ancient Grudge")]
public static class AncientGrudgeFactory
{
    public const string CardName = "Ancient Grudge";
    public const string Slug = "ancient-grudge";
    public const string PrintedManaCost = "{1}{R}";
    public const string FlashbackManaCost = "{G}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "destroy target artifact" <see cref="SpellDefinition"/>
    /// declaratively (the shared <c>destroy_target</c> verb on the
    /// <c>artifact</c> target filter). The candidate gatherer restricts targets
    /// to artifacts (CR 301), and CR 608.2b re-checks the SAME
    /// <see cref="TargetFilters"/> predicate at resolution, so a target whose
    /// type changed before resolution fizzles cleanly. Mirrors
    /// <see cref="ShatterFactory"/> / <see cref="SmeltFactory"/>.
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

    /// <summary>
    /// Build the <see cref="FlashbackAlternativeCost"/> for Ancient Grudge —
    /// the printed Flashback {G} (CR 702.34). Callers cast Ancient Grudge from
    /// the graveyard by passing this alt-cost to the spell-cast flow; the
    /// post-resolve exile (CR 702.34b) is handled by
    /// <see cref="FlashbackAlternativeCost.OnResolved"/>.
    /// </summary>
    public static FlashbackAlternativeCost GetFlashbackAlternativeCost() =>
        new(ManaCost.Parse(FlashbackManaCost));
}
