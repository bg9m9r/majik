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
/// <para><b>Engine wiring status (2026-05):</b> the data type ships now
/// so factories (Cavern, Eldrazi Temple, future Workshop) can stamp
/// their restrictions on the generated <see cref="ManaAbility"/>. The
/// payment-gate side — filtering tagged mana out of
/// <see cref="Majik.Core.ValueObjects.ManaPool"/> entries during
/// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> — is deferred
/// because <c>ManaPool</c> stores bucketed colour counts, not slot-level
/// provenance; flipping that surface is a separate slice. See
/// <c>MODERN_COVERAGE.md</c>.</para>
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
    /// Construct a spend-restriction.
    /// </summary>
    /// <param name="description">Non-empty human-readable label.</param>
    /// <param name="predicate">Predicate against the spell to be paid.</param>
    public SpendRestriction(string description, Func<ISpell, bool> predicate)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description must be non-empty.", nameof(description));
        }
        Description = description;
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    /// <summary>
    /// Evaluate the restriction against a candidate spell. Returns
    /// <c>true</c> when the tagged mana is permitted to pay a pip on
    /// <paramref name="spell"/>. Null-safe: returns <c>false</c> on a
    /// null spell (no spell context ⇒ no permission).
    /// </summary>
    public bool SatisfiedBy(ISpell? spell) => spell != null && Predicate(spell);

    public bool Equals(SpendRestriction? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Description == other.Description && Predicate == other.Predicate;
    }

    public override bool Equals(object? obj) => Equals(obj as SpendRestriction);

    public override int GetHashCode() => HashCode.Combine(Description, Predicate);

    public override string ToString() => $"SpendRestriction(\"{Description}\")";
}
