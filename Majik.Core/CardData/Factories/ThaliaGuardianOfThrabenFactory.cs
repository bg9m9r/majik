using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thalia, Guardian of Thraben
/// (Dark Ascension — Legendary Creature — Human Soldier {1}{W} 2/1).
///
/// Oracle text:
///   "First strike.
///    Noncreature spells cost {1} more to cast."
///
/// ## Implementation
///
/// ### First Strike (CR 702.7)
/// Wired as a <see cref="KeywordAbility"/> marker. Combat damage assignment
/// for first-strike creatures is read by the combat system.
///
/// ### "Noncreature spells cost {1} more to cast." (CR 117.7 / CR 601.2f)
/// Wired via <see cref="SpellCostIncreaseAbility"/> on the card.
/// Predicate: <c>!card.HasType(CardType.Creature)</c> — matches any spell
/// that is NOT a Creature spell (Instants, Sorceries, Artifacts, Enchantments,
/// Planeswalkers, etc.).
/// Increase: a flat {1} generic per cast (symmetric — applies to both
/// players' noncreature spells, same as Damping Sphere's per-cast rider).
/// <see cref="CostReduction.GetEffectiveCost(ICard, Player,
/// IEnumerable{Player}?)"/> scans every player's battlefield for
/// <see cref="SpellCostIncreaseAbility"/> riders, so opposing copies of
/// Thalia also tax the caster.
///
/// The single-arg <see cref="Create(Player)"/> overload attaches the
/// <see cref="SpellCostIncreaseAbility"/> for card-shape tests; the
/// <see cref="Create(Player, ContinuousEffectsService?)"/> overload is the
/// canonical production path (the cost increase already lives on the card's
/// Abilities list and is picked up by <see cref="CostReduction.GetEffectiveCost"/>
/// — no separate continuous-effects registration is required).
///
/// ## Deferred
/// - LTB unregister: the <see cref="SpellCostIncreaseAbility"/> on the card
///   becomes inert when Thalia is off the battlefield (the
///   <see cref="CostReduction.GetEffectiveCost"/> scanner only walks
///   battlefield permanents), so the cost rider lifts automatically without
///   an explicit unregister step.
/// </summary>
[CardName("Thalia, Guardian of Thraben")]
public static class ThaliaGuardianOfThrabenFactory
{
    public const string CardName = "Thalia, Guardian of Thraben";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Thalia with the correct card shape — Legendary 2/1 Human
    /// Soldier, First Strike keyword, and the noncreature-spell cost-increase
    /// rider attached as static metadata. Suitable for shape / dispatcher tests
    /// and for production use (no live continuous-effects registration needed
    /// for the cost rider).
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Thalia, Guardian of Thraben. The
    /// <paramref name="continuousEffects"/> parameter is accepted for API
    /// symmetry with other lord-style factories but is not used — Thalia's
    /// noncreature-spell cost increase is modelled as a
    /// <see cref="SpellCostIncreaseAbility"/> on the card itself, which
    /// <see cref="CostReduction.GetEffectiveCost"/> picks up by scanning
    /// battlefield permanents. No separate layers-service registration is
    /// required.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Unused; accepted for API consistency
    /// with other factories. May be null.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.7 — First Strike. KeywordAbility marker; the combat system
        // reads it when assigning damage in the first-strike damage step.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        // CR 117.7 / CR 601.2f — "Noncreature spells cost {1} more to cast."
        // Flat +{1} generic per cast; predicate excludes Creature spells so
        // that creature spells are not affected. Symmetric — taxes any
        // caster's noncreature spells while Thalia is on the battlefield.
        // CostReduction.GetEffectiveCost walks all players' battlefields for
        // SpellCostIncreaseAbility riders, so the increase fires regardless
        // of whose turn it is or which player is casting.
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: c => !c.HasType(CardType.Creature),
            extraGeneric: (_, _) => 1,
            description: "Noncreature spells cost {1} more to cast."));

        return card;
    }
}
