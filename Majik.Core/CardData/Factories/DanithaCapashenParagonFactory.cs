using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Danitha Capashen, Paragon (Dominaria, {2}{W}).
/// Legendary Creature — Human Knight 2/2. Oracle text (verified against
/// Scryfall):
///   "First strike, vigilance, lifelink
///    Aura and Equipment spells you cast cost {1} less to cast."
///
/// The base shape (name, Legendary supertype, Creature, Human + Knight
/// subtypes, {2}{W}, 2/2) is materialised from the embedded JSON definition
/// (<c>danitha-capashen-paragon.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The keyword soup and the
/// Aura/Equipment cost reducer are layered on here — the JSON
/// <c>AbilityDefinition</c> schema carries neither evergreen keyword markers
/// nor a parameterised spell-cost reducer (same posture as
/// <see cref="AdelineResplendentCatharFactory"/> for keyword markers and
/// <see cref="CloudKeyFactory"/> for the cost reducer).
///
/// ## Implemented (v1)
///
/// - <b>First strike (CR 702.7), Vigilance (CR 702.21), Lifelink
///   (CR 702.15)</b> — three <see cref="KeywordAbility"/> markers so
///   <c>ICard.Abilities</c> reflects the printed line and the combat lookups
///   in <see cref="Majik.Core.Combat.CombatAbilities"/> (which read off the
///   keyword markers) match. Canonical keyword strings ("First strike",
///   "Vigilance", "Lifelink") match the layer-system keyword set exactly.
///
/// - <b>"Aura and Equipment spells you cast cost {1} less to cast."
///   (CR 117.7)</b> — wired via <see cref="SpellCostReductionAbility"/>, the
///   same subtractive-rider shape Cloud Key / Etherium Sculptor / Goblin
///   Electromancer use. The predicate gates on the spell carrying the Aura
///   (CR 205.3h) or Equipment (CR 205.3g) subtype; the reduction is a flat 1
///   generic per cast.
///   <see cref="Majik.Core.Costs.CostReduction.GetEffectiveCost"/> scans only
///   the caster's battlefield for this ability shape, so the "you cast" scope
///   (CR 117.7) is enforced by the cost-calc helper — only the controller of
///   this Danitha benefits. Coloured pips are untouched (CR 117.7c) and the
///   cost floors at zero inside the cost-calc helper.
/// </summary>
[CardName("Danitha Capashen, Paragon")]
public static class DanithaCapashenParagonFactory
{
    public const string CardName = "Danitha Capashen, Paragon";
    public const string Slug = "danitha-capashen-paragon";

    /// <summary>Granted keywords — canonical strings matching the combat
    /// lookups in <see cref="Majik.Core.Combat.CombatAbilities"/>.</summary>
    public const string FirstStrike = "First strike";
    public const string Vigilance = "Vigilance";
    public const string Lifelink = "Lifelink";

    /// <summary>
    /// Construct Danitha. The keyword markers and the Aura/Equipment cost
    /// reducer are always attached — there is no live runtime service needed
    /// (the reducer is a passive battlefield rider read by
    /// <see cref="Majik.Core.Costs.CostReduction"/>; the keywords are static
    /// markers). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Human + Knight, {2}{W}, 2/2). No abilities in the JSON —
        // keyword markers + cost reducer layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.7 / 702.21 / 702.15 — printed evergreen keyword markers.
        card.AddAbility(new KeywordAbility(FirstStrike, card, owner));
        card.AddAbility(new KeywordAbility(Vigilance, card, owner));
        card.AddAbility(new KeywordAbility(Lifelink, card, owner));

        // CR 117.7 — "Aura and Equipment spells you cast cost {1} less to
        // cast." Predicate gates on the Aura (CR 205.3h) / Equipment
        // (CR 205.3g) subtype; reduction is a flat 1 generic. The "you cast"
        // scope is enforced by CostReduction.GetEffectiveCost, which scans
        // only the caster's battlefield for this rider.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasSubtype(CardSubtype.Aura)
                || c.HasSubtype(CardSubtype.Equipment),
            reduction: (_, _) => 1,
            description: "Aura and Equipment spells you cast cost {1} less to cast."));

        return card;
    }
}
