using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thorn of Amethyst (Future Sight — Artifact {2}).
///
/// Oracle text (verified against Scryfall):
///   "Noncreature spells cost {1} more to cast."
///
/// ## Implementation
///
/// Mechanically identical to the noncreature-spell tax on
/// <see cref="ThaliaGuardianOfThrabenFactory"/> — the same
/// <see cref="SpellCostIncreaseAbility"/> rider lifted onto an Artifact shell
/// instead of a creature body.
///
/// ### Base shape
/// Materialised from the embedded JSON definition
/// (<c>thorn-of-amethyst.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — an Artifact at {2}. The cost
/// rider is layered on here.
///
/// ### "Noncreature spells cost {1} more to cast." (CR 117.7 / CR 601.2f)
/// Wired via <see cref="SpellCostIncreaseAbility"/> on the card.
/// Predicate: <c>!card.HasType(CardType.Creature)</c> — matches any spell
/// that is NOT a Creature spell (Instants, Sorceries, Artifacts, Enchantments,
/// Planeswalkers, etc.).
/// Increase: a flat {1} generic per cast (symmetric — applies to BOTH
/// players' noncreature spells, same as Thalia's per-cast rider).
/// <see cref="CostReduction.GetEffectiveCost(ICard, Player,
/// IEnumerable{Player}?)"/> scans every player's battlefield for
/// <see cref="SpellCostIncreaseAbility"/> riders, so an opposing Thorn of
/// Amethyst also taxes the caster.
///
/// CR 117.7c — only the generic portion of the cost is increased; coloured
/// pips are untouched.
///
/// ## Deferred
/// - LTB unregister: the <see cref="SpellCostIncreaseAbility"/> on the card
///   becomes inert when Thorn of Amethyst is off the battlefield (the
///   <see cref="CostReduction.GetEffectiveCost"/> scanner only walks
///   battlefield permanents), so the cost rider lifts automatically without
///   an explicit unregister step.
/// </summary>
[CardName("Thorn of Amethyst")]
public static class ThornOfAmethystFactory
{
    public const string CardName = "Thorn of Amethyst";
    public const string Slug = "thorn-of-amethyst";

    /// <summary>
    /// Construct Thorn of Amethyst with the correct card shape (Artifact {2})
    /// and the noncreature-spell cost-increase rider attached as static
    /// metadata. Suitable for shape / dispatcher tests and for production use
    /// (no live continuous-effects registration needed for the cost rider —
    /// <see cref="CostReduction.GetEffectiveCost"/> picks it up by scanning
    /// battlefield permanents). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Artifact, {2}).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // CR 117.7 / CR 601.2f — "Noncreature spells cost {1} more to cast."
        // Flat +{1} generic per cast; predicate excludes Creature spells so
        // that creature spells are not affected. Symmetric — taxes any
        // caster's noncreature spells while Thorn of Amethyst is on the
        // battlefield. CostReduction.GetEffectiveCost walks all players'
        // battlefields for SpellCostIncreaseAbility riders, so the increase
        // fires regardless of whose turn it is or which player is casting.
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: c => !c.HasType(CardType.Creature),
            extraGeneric: (_, _) => 1,
            description: "Noncreature spells cost {1} more to cast."));

        return card;
    }
}
