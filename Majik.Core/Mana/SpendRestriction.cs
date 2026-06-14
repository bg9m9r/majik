using Majik.Core.Spells;

namespace Majik.Core.Mana;

/// <summary>
/// A "spend this mana only on X" rider attached to a unit of generated
/// mana. Cavern of Souls ("Spend this mana only to cast a creature spell
/// of the chosen type"), Eldrazi Temple ("Spend this mana only to cast
/// Eldrazi spells or activate abilities of Eldrazi"), and Mishra's
/// Workshop ("Spend this mana only to cast artifact spells") all share
/// this shape.
///
/// <para>Lifecycle: when a <see cref="Majik.Core.Abilities.ManaAbility"/>
/// is constructed with a restriction, every unit of mana it generates
/// carries that restriction via a <see cref="ManaTag"/>. The payment
/// resolver later asks <see cref="SatisfiedBy(ISpell)"/> with the spell
/// being cast — if the predicate returns <c>false</c>, the tagged mana
/// is ineligible to pay any pip on that spell.</para>
///
/// <para><b>Engine wiring status (2026-06):</b> both halves now ship.
/// Factories (Cavern, Eldrazi Temple, Ancient Ziggurat) stamp their
/// restriction on the generated <see cref="ManaAbility"/>; the restriction
/// rides each produced colored unit into the per-slot
/// <see cref="ManaProvenanceSlot"/> ledger, and the payment gate in
/// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> treats restricted
/// mana the spell doesn't satisfy as UNAVAILABLE — it removes those colored
/// units from the spendable pool before checking payability and withholds
/// them across the actual (bucketed) spend so they can't pay a non-matching
/// pip. <c>ManaPool</c> still stores bucketed colour counts; the slot-level
/// restriction lives in the parallel provenance ledger rather than on the
/// pool. CR 106.4 — the restriction applies at spend time.</para>
///
/// <para>Value-typed: two restrictions with the same description and
/// predicate-reference compare equal. Predicates are by-reference compared
/// (delegates) — callers that need structural equality should reuse the
/// shared static factories below (e.g. predicate captured in a static
/// readonly field) rather than allocating new closures per call.</para>
/// </summary>
public sealed class SpendRestriction : IEquatable<SpendRestriction>
{
    /// <summary>
    /// Human-readable description of the restriction (e.g. "creature
    /// spell", "Eldrazi spell or ability"). Used in logs, UI strings,
    /// and the debugger; does NOT participate in enforcement.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Predicate evaluated against the spell being cast. <c>true</c> ⇒
    /// the tagged mana may pay a pip on this spell; <c>false</c> ⇒
    /// ineligible (stays in the pool, must be covered by other mana).
    /// CR 106.4 — restrictions apply at spend time, not generation time.
    /// </summary>
    public Func<ISpell, bool> Predicate { get; }

    /// <summary>
    /// Optional predicate evaluated against an ABILITY-ACTIVATION cost spend
    /// (the <see cref="ManaSpendContext.ForAbilityCost"/> shape). Models the
    /// "or activate abilities of X" / "only to activate abilities of land
    /// sources" half of a restriction (Eldrazi Temple, Sunken Citadel) — a
    /// surface the spell-only <see cref="Predicate"/> can't express. <c>null</c>
    /// ⇒ the restriction permits NO ability spend (the conservative default the
    /// spell-only restrictions used: Ancient Ziggurat / Cavern of Souls only
    /// permit spells). CR 106.4 — the restriction names which kind of spend is
    /// permitted.
    /// </summary>
    public Func<ManaSpendContext, bool>? AbilityPredicate { get; }

    /// <summary>
    /// Construct a spend-restriction.
    /// </summary>
    /// <param name="description">Non-empty human-readable label.</param>
    /// <param name="predicate">Predicate against the spell to be paid.</param>
    /// <param name="abilityPredicate">Optional predicate against an
    /// ability-activation cost spend (the "or activate abilities of X" half).
    /// <c>null</c> ⇒ no ability spend is permitted.</param>
    public SpendRestriction(
        string description,
        Func<ISpell, bool> predicate,
        Func<ManaSpendContext, bool>? abilityPredicate = null)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description must be non-empty.", nameof(description));
        }
        Description = description;
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        AbilityPredicate = abilityPredicate;
    }

    /// <summary>
    /// Evaluate the restriction against a candidate spell. Returns
    /// <c>true</c> when the tagged mana is permitted to pay a pip on
    /// <paramref name="spell"/>. Null-safe: returns <c>false</c> on a
    /// null spell (no spell context ⇒ no permission).
    /// </summary>
    public bool SatisfiedBy(ISpell? spell) => spell != null && Predicate(spell);

    /// <summary>
    /// CR 106.4 — evaluate the restriction against an ABILITY-COST spend
    /// (<paramref name="context"/> must be an
    /// <see cref="ManaSpendContext.IsAbilitySpend"/> context). Returns
    /// <c>true</c> only when this restriction carries an
    /// <see cref="AbilityPredicate"/> AND that predicate accepts the context.
    /// A restriction with no ability predicate permits no ability spend.
    /// </summary>
    public bool SatisfiedBy(ManaSpendContext context) =>
        context.IsAbilitySpend && AbilityPredicate is not null && AbilityPredicate(context);

    public bool Equals(SpendRestriction? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Description == other.Description
               && Predicate == other.Predicate
               && AbilityPredicate == other.AbilityPredicate;
    }

    public override bool Equals(object? obj) => Equals(obj as SpendRestriction);

    public override int GetHashCode() => HashCode.Combine(Description, Predicate, AbilityPredicate);

    public override string ToString() => $"SpendRestriction(\"{Description}\")";
}
