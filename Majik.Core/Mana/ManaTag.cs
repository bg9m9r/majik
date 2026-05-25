using Majik.Core.Spells;
using Majik.Core.ValueObjects;

namespace Majik.Core.Mana;

/// <summary>
/// Provenance tag for a single unit of generated mana — pairs the mana's
/// <see cref="ManaColor"/> with an optional <see cref="SpendRestriction"/>
/// rider. Cavern of Souls, Eldrazi Temple, Mishra's Workshop, Pillar of
/// the Paruns, etc. all generate <c>ManaTag</c>-flagged mana; vanilla
/// lands (Forest, Mountain) generate untagged mana (i.e.
/// <see cref="Restriction"/> is <c>null</c>).
///
/// <para>Why a separate type from <see cref="Majik.Core.ValueObjects.ManaCost"/>:
/// <c>ManaCost</c> is a multiset bucketed by colour and is shared between
/// the cost printed on a card and the floating-mana representation in
/// <see cref="Majik.Core.ValueObjects.ManaPool"/>. A spend-restriction
/// only makes sense per-slot of generated mana — bucketing erases the
/// per-slot identity. <c>ManaTag</c> is the per-slot value-object the
/// payment resolver will eventually consume when ManaPool's internal
/// representation moves from buckets-of-counts to a list-of-tags.</para>
///
/// <para><b>Engine wiring status (2026-05):</b> data type only.
/// <see cref="Majik.Core.Abilities.ManaAbility"/> exposes a
/// <c>SpendRestriction?</c> slot so factories can stamp the rider; the
/// payment-gate side (filtering tagged entries in
/// <see cref="Majik.Core.Costs.ManaPaymentResolver"/>) is deferred — see
/// <see cref="SpendRestriction"/> xmldoc.</para>
/// </summary>
public sealed class ManaTag : IEquatable<ManaTag>
{
    /// <summary>Colour of the generated mana unit.</summary>
    public ManaColor Color { get; }

    /// <summary>
    /// Optional spend-restriction rider. <c>null</c> ⇒ vanilla mana,
    /// spendable on any cost (CR 106.4 default).
    /// </summary>
    public SpendRestriction? Restriction { get; }

    /// <summary>
    /// Construct a mana tag. <paramref name="restriction"/> may be null
    /// for vanilla (untagged) mana — the type is uniformly used for both
    /// so callers can stash a tag list per pool entry without
    /// special-casing.
    /// </summary>
    public ManaTag(ManaColor color, SpendRestriction? restriction = null)
    {
        Color = color;
        Restriction = restriction;
    }

    /// <summary>
    /// Whether this tagged mana may pay a pip on <paramref name="spell"/>.
    /// Untagged mana ⇒ always <c>true</c>. Tagged ⇒ delegates to the
    /// restriction's predicate. Null spell ⇒ <c>false</c> for tagged
    /// mana (no spell context ⇒ no permission), <c>true</c> for
    /// untagged (e.g. paying an ability activation cost).
    /// </summary>
    public bool CanSpendOn(ISpell? spell)
    {
        if (Restriction is null) return true;
        return Restriction.SatisfiedBy(spell);
    }

    public bool Equals(ManaTag? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Color == other.Color
               && Equals(Restriction, other.Restriction);
    }

    public override bool Equals(object? obj) => Equals(obj as ManaTag);

    public override int GetHashCode() => HashCode.Combine(Color, Restriction);

    public override string ToString() =>
        Restriction is null ? Color.ToString() : $"{Color} [{Restriction.Description}]";
}
