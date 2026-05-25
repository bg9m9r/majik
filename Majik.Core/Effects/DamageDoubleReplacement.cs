namespace Majik.Core.Effects;

/// <summary>
/// CR 614 / CR 615 — generic "double a damage intent" replacement.
/// Wraps a caller-supplied predicate over <see cref="DamageIntent"/>; when
/// the predicate returns <c>true</c>, the intent is rewritten with twice
/// the original <see cref="DamageIntent.Amount"/>. Backs the
/// damage-doubling pillar:
///
///   - <b>Inquisitor's Flail</b> (CR 510.1c combat-damage doubling)
///     — two registrations gated on equipped-creature source / target
///     AND <see cref="DamageIntent.IsCombatDamage"/>.
///   - <b>Furnace of Rath</b> — single registration with an
///     always-true predicate; doubles every damage intent symmetrically
///     (no source / target / combat filter).
///   - <b>Angrath's Marauders</b> — single registration gated on
///     source-controller = card-controller AND target = opponent-or-
///     opponent-permanent.
///   - <b>Gisela, Blade of Goldnight</b> (doubling half) — single
///     registration with Angrath's gate; paired with
///     <see cref="DamageHalveRoundedUpReplacement"/> for the
///     half-rounded-up clause.
///
/// Per-effect dedup in <see cref="ReplacementBus.Apply{TIntent}"/>
/// (CR 616.1c) lets multiple instances stack: two copies of Furnace of
/// Rath quadruple, Furnace + Gisela's Angrath-half quadruple opponent
/// damage, etc.
///
/// The effect is non-OneShot (sticks around for repeated firings while
/// the source permanent is on the battlefield) and uses
/// <see cref="object"/> identity (`this`) for the dedup tag, so multiple
/// independent registrations from distinct card sources each fire once
/// per intent.
/// </summary>
public sealed class DamageDoubleReplacement : IReplacementEffect<DamageIntent>
{
    private readonly Func<DamageIntent, bool> _predicate;

    /// <summary>
    /// Construct a doubling replacement gated by <paramref name="predicate"/>.
    /// Caller is responsible for any source / target / controller / combat
    /// gates — the bus-side guard only short-circuits on
    /// <c>Amount &lt;= 0</c>.
    /// </summary>
    public DamageDoubleReplacement(Func<DamageIntent, bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (intent.Amount <= 0) return false;
        return _predicate(intent);
    }

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
        => intent with { Amount = intent.Amount * 2 };
}
