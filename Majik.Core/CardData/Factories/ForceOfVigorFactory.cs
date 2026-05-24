using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.SpellTemplates.Templates.Destroy;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Force of Vigor (Modern Horizons, {2}{G}{G}).
///
/// Instant. Oracle text:
///   "If it's not your turn, you may exile a green card from your hand
///    rather than pay this spell's mana cost.
///    Destroy up to two target artifacts and/or enchantments."
///
/// Implemented in v1:
///   * Instant card shape ({2}{G}{G}, Green) — built via the fluent
///     <see cref="CardDef"/> DSL.
///   * Destroy up to two target artifacts and/or enchantments — built via
///     <see cref="BuildDefinition"/>, delegating to the existing shared
///     destroy-up-to factory used by
///     <see cref="DestroyUpToArtifactEnchantmentTemplate"/>. CR 601.2c +
///     CR 608.2b semantics intact.
///   * Pitch alternative cost (<see cref="Majik.Core.Costs.PitchAlternativeCost"/>).
///   * Bot probe — <see cref="PitchAltCostProbe"/> recognizes this card.
///
/// Reminder: the Force-of-cycle pitch is CR 118.9 (alternative cost).
/// </summary>
[CardName("Force of Vigor")]
public static class ForceOfVigorFactory
{
    public const string CardName = "Force of Vigor";

    /// <summary>Force of Vigor destroys "up to two" targets (CR 601.2c).</summary>
    public const int MaxTargets = 2;

    public static CardDef Define() => CardDef.Instant(CardName, "{2}{G}{G}");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>Build the "destroy up to two target artifacts and/or enchantments"
    /// SpellDefinition. Delegates to the shared destroy-up-to factory used by
    /// the data-driven oracle template.</summary>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver) =>
        DestroySpellFactory.DestroyUpToArtifactEnchantmentSpell(targetResolver, MaxTargets);
}
