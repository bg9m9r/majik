using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lodestone Golem
/// (Worldwake — Artifact Creature — Golem {4} 5/3).
///
/// Oracle text:
///   "Nonartifact spells cost {1} more to cast."
///
/// ## Implementation
///
/// ### Card shape (CR 301.1 / CR 302.1 — Artifact Creature)
/// 5/3 Golem with printed mana cost {4}. The Artifact type is layered on via
/// <see cref="Card.AddCardType"/> so both <c>HasType(Artifact)</c> and
/// <c>HasType(Creature)</c> pass — same shape as
/// <see cref="MyrEnforcerFactory"/>.
///
/// ### "Nonartifact spells cost {1} more to cast." (CR 117.7 / CR 601.2f)
/// Wired via <see cref="SpellCostIncreaseAbility"/> on the card — identical
/// shape to <see cref="ThaliaGuardianOfThrabenFactory"/>'s noncreature-spell
/// rider, but the predicate excludes Artifacts instead of Creatures:
/// <c>!card.HasType(CardType.Artifact)</c> — matches any spell that is NOT an
/// Artifact spell (Instants, Sorceries, Creatures, Enchantments, Planeswalkers,
/// etc.). The per-cast delta is a flat {1} generic. Symmetric — applies to
/// both players' nonartifact spells.
/// <see cref="CostReduction.GetEffectiveCost(ICard, Player,
/// IEnumerable{Player}?)"/> scans every player's battlefield for
/// <see cref="SpellCostIncreaseAbility"/> riders, so opposing copies of
/// Lodestone Golem also tax the caster and two copies stack additively.
///
/// ## Deferred
/// - LTB unregister: the <see cref="SpellCostIncreaseAbility"/> on the card
///   becomes inert when Lodestone Golem is off the battlefield (the
///   <see cref="CostReduction.GetEffectiveCost"/> scanner only walks
///   battlefield permanents), so the cost rider lifts automatically without an
///   explicit unregister step.
/// </summary>
[CardName("Lodestone Golem")]
public static class LodestoneGolemFactory
{
    public const string CardName = "Lodestone Golem";
    public const string PrintedManaCost = "{4}";
    public const int Power = 5;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Lodestone Golem with the correct card shape — a 5/3 Artifact
    /// Creature — Golem with the nonartifact-spell cost-increase rider attached
    /// as static metadata. Suitable for shape / dispatcher tests and for
    /// production use (no live continuous-effects registration needed for the
    /// cost rider — <see cref="CostReduction.GetEffectiveCost"/> picks it up by
    /// scanning battlefield permanents).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Golem });

        // CR 301.1 / 302.1 — Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 / CR 601.2f — "Nonartifact spells cost {1} more to cast."
        // Flat +{1} generic per cast; predicate excludes Artifact spells so
        // that artifact spells are not affected. Symmetric — taxes any caster's
        // nonartifact spells while Lodestone Golem is on the battlefield.
        // CostReduction.GetEffectiveCost walks all players' battlefields for
        // SpellCostIncreaseAbility riders, so the increase fires regardless of
        // whose turn it is or which player is casting.
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: c => !c.HasType(CardType.Artifact),
            extraGeneric: (_, _) => 1,
            description: "Nonartifact spells cost {1} more to cast."));

        return card;
    }
}
