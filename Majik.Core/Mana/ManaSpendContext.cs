using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Spells;

namespace Majik.Core.Mana;

/// <summary>
/// CR 106.4 — the object a unit of mana is being SPENT ON, evaluated against a
/// <see cref="SpendRestriction"/> at payment time. A spend is one of three
/// shapes:
/// <list type="bullet">
/// <item><b>A spell being cast</b> (<see cref="ForSpell"/>) — the
/// spell-cast path. The restriction's spell predicate decides eligibility
/// (Cavern of Souls "creature spell of the chosen type", Ancient Ziggurat
/// "creature spell", Eldrazi Temple "Eldrazi spell").</item>
/// <item><b>An ability activation</b> (<see cref="ForAbilityCost"/>) — the
/// ability-cost path. Carries the ability's SOURCE object so the restriction's
/// ability predicate can read the source's types/subtypes (Sunken Citadel
/// "abilities of land sources", Eldrazi Temple "or activate abilities of
/// Eldrazi").</item>
/// <item><b>No context</b> (<see cref="None"/>) — a non-spell, non-typed
/// payment (or a caller that hasn't threaded a context). Restricted mana is
/// treated as UNAVAILABLE for a None spend (CR 106.4 — a restriction only
/// permits the named kind of spend), while unrestricted mana pays freely.</item>
/// </list>
///
/// <para>Why a discriminated context instead of just an <see cref="ISpell"/>:
/// the existing payment gate threaded only a spell, so the "or activate
/// abilities of X" half of a restriction (Eldrazi Temple, Sunken Citadel) had
/// no surface to read and the ability-cost path bypassed the gate entirely.
/// This carries enough to evaluate BOTH halves at the one spend site.</para>
/// </summary>
public readonly struct ManaSpendContext
{
    /// <summary>The spell being cast, when this is a spell-cast spend.</summary>
    public ISpell? Spell { get; }

    /// <summary>
    /// The SOURCE of the activated ability whose cost is being paid, when this
    /// is an ability-cost spend (e.g. the land/creature/artifact whose
    /// <c>{T}: …</c> ability is being activated). <c>null</c> for a spell spend
    /// or <see cref="None"/>.
    /// </summary>
    public ICard? AbilitySource { get; }

    /// <summary>
    /// <c>true</c> when this is an ability-cost spend (even if
    /// <see cref="AbilitySource"/> happens to be null — a source-less ability
    /// still counts as an ability spend, not a spell spend).
    /// </summary>
    public bool IsAbilitySpend { get; }

    private ManaSpendContext(ISpell? spell, ICard? abilitySource, bool isAbilitySpend)
    {
        Spell = spell;
        AbilitySource = abilitySource;
        IsAbilitySpend = isAbilitySpend;
    }

    /// <summary>A spend with no spell and no ability context (CR 106.4 — a
    /// restriction permits no such spend). Restricted mana is unavailable.</summary>
    public static ManaSpendContext None => new(null, null, isAbilitySpend: false);

    /// <summary>A spell-cast spend. The restriction's spell predicate decides
    /// eligibility.</summary>
    public static ManaSpendContext ForSpell(ISpell spell) =>
        new(spell ?? throw new ArgumentNullException(nameof(spell)), null, isAbilitySpend: false);

    /// <summary>An ability-activation cost spend, carrying the ability's source
    /// object so a restriction's ability predicate can inspect it. The
    /// <paramref name="abilitySource"/> may be null when the activator can't
    /// surface a card source.</summary>
    public static ManaSpendContext ForAbilityCost(ICard? abilitySource) =>
        new(null, abilitySource, isAbilitySpend: true);

    /// <summary>
    /// CR 106.4 — whether mana carrying <paramref name="restriction"/> may be
    /// spent under this context. Unrestricted mana (<paramref name="restriction"/>
    /// null) is always spendable. A spell spend defers to the restriction's
    /// spell predicate; an ability spend defers to its ability predicate; a
    /// <see cref="None"/> spend is never permitted for restricted mana.
    /// </summary>
    public bool Permits(SpendRestriction? restriction)
    {
        if (restriction is null) return true;
        if (IsAbilitySpend) return restriction.SatisfiedBy(this);
        return restriction.SatisfiedBy(Spell);
    }

    /// <summary>Convenience: does the ability source carry
    /// <paramref name="type"/>? <c>false</c> when this isn't an ability spend or
    /// the source is null. Used by restriction ability-predicates (Sunken
    /// Citadel "land sources").</summary>
    public bool SourceHasType(CardType type) =>
        AbilitySource is not null && AbilitySource.HasType(type);

    /// <summary>Convenience: does the ability source carry
    /// <paramref name="subtype"/>? <c>false</c> when this isn't an ability spend
    /// or the source is null. Used by restriction ability-predicates (Eldrazi
    /// Temple "abilities of Eldrazi").</summary>
    public bool SourceHasSubtype(CardSubtype subtype) =>
        AbilitySource is not null && AbilitySource.HasSubtype(subtype);
}
