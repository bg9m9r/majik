using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mishra's Workshop (Antiquities).
///
/// Land. Oracle text:
///   "{T}: Add {C}{C}{C}. Spend this mana only to cast artifact spells."
///
/// ## Implemented (v1)
/// - Single tap mana ability adding three colourless (engine buckets
///   {C} as +1 generic per CR 107.4c — see
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>).
///
/// Migrated to the fluent <see cref="CardDef"/> DSL.
///
/// ## Deferred — restriction "spend this mana only to cast artifact spells"
/// CR 106.4 covers per-mana spending restrictions. Enforcing it requires
/// a per-mana provenance ledger that tags each unit of mana with its
/// source + restriction predicate. No such ledger exists today; the v1
/// shell ships the structural mana amount without the artifact-only gate.
///
/// ## Types
/// - Plain Land. No supertypes, no land subtypes.
/// </summary>
[CardName("Mishra's Workshop")]
public static class MishrasWorkshopFactory
{
    public static CardDef Define() => CardDef
        .Land("Mishra's Workshop")
        // {T}: Add {C}{C}{C}. Artifact-only spend restriction is
        // structural-only in v1 (no provenance ledger — see xmldoc).
        .ManaAbility("CCC");

    public static Land Create(Player owner) =>
        (Land)CardDefRuntime.Build(Define(), owner);
}
