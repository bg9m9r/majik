using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates.Templates.Destroy;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

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
///   * Instant card shape ({2}{G}{G}, Green).
///   * Destroy up to two target artifacts and/or enchantments — built via
///     <see cref="BuildDefinition"/>, which delegates to the existing
///     <c>DestroySpellFactory.DestroyUpToArtifactEnchantmentSpell</c> body
///     (the same SpellDefinition reused by
///     <see cref="DestroyUpToArtifactEnchantmentTemplate"/>). CR 601.2c —
///     "up to two" allows 0 through 2 legal targets; resolution filters
///     each target to Artifact/Enchantment per CR 608.2b.
///   * Pitch alternative cost (<see cref="Majik.Core.Costs.PitchAlternativeCost"/>):
///     not-your-turn + exile a green card from hand. No life rider. The
///     cast flow checks the timing predicate via
///     <see cref="Majik.Core.Costs.PitchAlternativeCost.IsLegalInContext(Player)"/>.
///   * Bot probe — <see cref="PitchAltCostProbe"/> recognizes this card by
///     name (Green, 0 life) and emits a candidate per green card in hand.
///
/// Reminder: the Force-of-cycle pitch is CR 118.9 (alternative cost) + a
/// timing rider that lives on <see cref="Majik.Core.Costs.PitchAlternativeCost"/>.
/// </summary>
[CardName("Force of Vigor")]
public static class ForceOfVigorFactory
{
    public const string CardName = "Force of Vigor";

    /// <summary>Force of Vigor destroys "up to two" targets (CR 601.2c).</summary>
    public const int MaxTargets = 2;

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, "{2}{G}{G}");
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>Build the "destroy up to two target artifacts and/or enchantments"
    /// SpellDefinition. Delegates to the shared destroy-up-to factory used by
    /// the data-driven oracle template, so the resolve behaviour is identical
    /// whether the spell is bound via NamedCardFactory dispatch or via the
    /// oracle-text template path.</summary>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver) =>
        DestroySpellFactory.DestroyUpToArtifactEnchantmentSpell(targetResolver, MaxTargets);
}
