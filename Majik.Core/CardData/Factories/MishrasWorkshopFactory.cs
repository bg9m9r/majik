using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mishra's Workshop (Antiquities).
///
/// Land. Oracle text:
///   "{T}: Add {C}{C}{C}. Spend this mana only to cast artifact spells."
///
/// ## Implemented (v1)
/// - Single tap mana ability via <see cref="ManaAbility"/> adding three
///   colourless (engine buckets {C} as +1 generic per CR 107.4c — see
///   <see cref="ManaCost.Parse"/>). Result: <c>ManaCost.Parse("CCC")</c>
///   produces a cost with <c>Generic == 3</c>, matching the printed
///   amount.
///
/// ## Deferred — restriction "spend this mana only to cast artifact spells"
/// CR 106.4 covers per-mana spending restrictions. Enforcing it requires
/// a per-mana provenance ledger that tags each unit of mana with its
/// source + restriction predicate and surfaces it through
/// <see cref="ManaPaymentResolver"/> at spell-cast time. No such ledger
/// exists today (see notes in <c>PyromancersGogglesFactory</c>,
/// <c>EngineeredExplosivesFactory</c>). Per the same gap acknowledged
/// across the codebase, the v1 shell ships the structural mana amount
/// without the artifact-only gate; once a provenance ledger lands, wire
/// the restriction here as a <c>spendableForPredicate</c>-style hook on
/// the <c>ManaAbility</c>.
///
/// ## Types
/// - Plain Land. No supertypes (not legendary, not basic). No printed
///   land subtypes.
/// </summary>
public static class MishrasWorkshopFactory
{
    /// <summary>
    /// Construct Mishra's Workshop owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            "Mishra's Workshop",
            supertypes: null,
            subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // {T}: Add {C}{C}{C}. The artifact-only spend restriction is
        // structural-only in v1 (no provenance ledger — see xmldoc).
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("CCC")));

        return land;
    }
}
