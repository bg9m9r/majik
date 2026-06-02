using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vryn Wingmare
/// (Magic Origins — Creature — Pegasus {2}{W} 2/1).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Flying
///    Noncreature spells cost {1} more to cast."
///
/// ## Why a named factory (functional reprint)
/// Vryn Wingmare is a functional reprint of
/// <see cref="ThaliaGuardianOfThrabenFactory"/>: the identical
/// "noncreature spells cost {1} more" static rider, with Flying instead of
/// First strike (and a plain 2/1 White Pegasus body rather than a Legendary
/// Human Soldier). Both abilities (a <see cref="KeywordAbility"/> marker and
/// a <see cref="SpellCostIncreaseAbility"/> static) already ship in the
/// engine — no new mechanic is required.
///
/// ## Implementation
///
/// ### Card shell (JSON)
/// The 2/1 Pegasus {2}{W} body is declared declaratively in
/// <c>Majik.Core/CardData/Cards/vryn-wingmare.json</c> and materialized via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> — the same posture as
/// <see cref="DeadlyDisputeFactory"/>. The JSON ability union only models
/// mana / activated / triggered abilities, so the two abilities below (a
/// keyword marker and a static cost-increase rider) are attached in C# after
/// the shell is built, exactly as Thalia does.
///
/// ### Flying (CR 702.9)
/// Wired as a <see cref="KeywordAbility"/> marker. The combat system reads it
/// when determining legal blocks (only creatures with flying or reach may
/// block a creature with flying — CR 509.1b / 702.9b).
///
/// ### "Noncreature spells cost {1} more to cast." (CR 117.7 / CR 601.2f)
/// Wired via <see cref="SpellCostIncreaseAbility"/> on the card.
/// Predicate: <c>!card.HasType(CardType.Creature)</c> — matches any spell
/// that is NOT a Creature spell (Instants, Sorceries, Artifacts,
/// Enchantments, Planeswalkers, etc.). Increase: a flat {1} generic per cast
/// (symmetric — applies to both players' noncreature spells, identical to
/// Thalia's rider).
/// <see cref="CostReduction.GetEffectiveCost(ICard, Player, IEnumerable{Player}?)"/>
/// scans every player's battlefield for
/// <see cref="SpellCostIncreaseAbility"/> riders, so an opposing Wingmare
/// also taxes the caster.
///
/// ## Deferred
/// - LTB unregister: the <see cref="SpellCostIncreaseAbility"/> on the card
///   becomes inert when Wingmare is off the battlefield (the
///   <see cref="CostReduction.GetEffectiveCost"/> scanner only walks
///   battlefield permanents), so the cost rider lifts automatically without
///   an explicit unregister step.
/// </summary>
[CardName("Vryn Wingmare")]
public static class VrynWingmareFactory
{
    public const string CardName = "Vryn Wingmare";
    public const string Slug = "vryn-wingmare";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Vryn Wingmare — a 2/1 Pegasus {2}{W} with Flying and the
    /// noncreature-spell cost-increase rider attached. The card shape comes
    /// from the embedded JSON definition; the two abilities the JSON schema
    /// cannot carry are attached in C# (the same split as
    /// <see cref="DeadlyDisputeFactory"/> / <see cref="ThaliaGuardianOfThrabenFactory"/>).
    /// Suitable for shape / dispatcher tests and for production use (no live
    /// continuous-effects registration is needed for the cost rider —
    /// <see cref="CostReduction.GetEffectiveCost"/> picks it up by scanning
    /// battlefield permanents).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);

        // CR 702.9 — Flying. KeywordAbility marker; the combat system reads it
        // when determining legal blocks (only flying/reach may block flyers).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 117.7 / CR 601.2f — "Noncreature spells cost {1} more to cast."
        // Flat +{1} generic per cast; predicate excludes Creature spells so
        // creature spells are not affected. Symmetric — taxes any caster's
        // noncreature spells while Wingmare is on the battlefield.
        // CostReduction.GetEffectiveCost walks all players' battlefields for
        // SpellCostIncreaseAbility riders, so the increase fires regardless of
        // whose turn it is or which player is casting.
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: c => !c.HasType(CardType.Creature),
            extraGeneric: (_, _) => 1,
            description: "Noncreature spells cost {1} more to cast."));

        return card;
    }
}
