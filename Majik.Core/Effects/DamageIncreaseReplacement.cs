namespace Majik.Core.Effects;

/// <summary>
/// CR 614 — generic "increase a damage intent by a flat amount"
/// replacement. Additive sibling to <see cref="DamageDoubleReplacement"/>
/// (which multiplies) and <see cref="DamageHalveRoundedUpReplacement"/>
/// (which halves). Wraps a caller-supplied predicate over
/// <see cref="DamageIntent"/>; when the predicate returns <c>true</c>, the
/// intent is rewritten with <c>Amount + Bonus</c>.
///
/// Backs "it deals that much damage plus N instead" oracle text
/// (Artist's Talent Level 3, Fiendish Duo, City on Fire's increment
/// clause family). The bonus is a flat constant captured at construction;
/// per-source / per-target gating is the predicate's responsibility, the
/// same contract as the doubling / halving siblings.
///
/// Per-effect dedup in <see cref="ReplacementBus.Apply{TIntent}"/>
/// (CR 616.1c) means the bonus fires at most once per intent per
/// registration, but multiple independent registrations (Artist's Talent
/// plus Furnace of Rath, say) each apply once and stack: a doubling
/// replacement applied to the rewritten intent doubles the already-
/// incremented amount, matching the "apply each replacement once, in some
/// order" reading of CR 616 (registration order wins in v1; full
/// CR 616.1 player-choice ordering is deferred — same posture as the
/// doubling family).
///
/// The effect is non-OneShot (sticks around for repeated firings while
/// the source permanent is on the battlefield) and uses
/// <see cref="object"/> identity (<c>this</c>) for the dedup tag, so
/// multiple registrations from distinct sources each fire once per intent.
/// </summary>
public sealed class DamageIncreaseReplacement : IReplacementEffect<DamageIntent>
{
    private readonly Func<DamageIntent, bool> _predicate;
    private readonly int _bonus;

    /// <summary>
    /// Construct an additive damage replacement gated by
    /// <paramref name="predicate"/>, adding <paramref name="bonus"/> to the
    /// intent's amount when it matches. Caller owns any source / target /
    /// controller / combat gates — the bus-side guard only short-circuits
    /// on <c>Amount &lt;= 0</c> (a source dealing 0 damage stays 0 —
    /// CR 120.8 / 614 only rewrites damage that would actually be dealt).
    /// </summary>
    public DamageIncreaseReplacement(Func<DamageIntent, bool> predicate, int bonus)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        if (bonus < 0) throw new ArgumentOutOfRangeException(nameof(bonus),
            "Damage-increase bonus must be non-negative; halving lives on DamageHalveRoundedUpReplacement.");
        _bonus = bonus;
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        // CR 614 — a "would deal damage" event that deals 0 isn't increased
        // (no damage event to replace). Matches the doubling sibling's guard.
        if (intent.Amount <= 0) return false;
        return _predicate(intent);
    }

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
        => intent with { Amount = intent.Amount + _bonus };
}
