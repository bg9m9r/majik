namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — generic "halve a damage intent, round up" prevention
/// replacement. Wraps a caller-supplied predicate over
/// <see cref="DamageIntent"/>; when the predicate returns <c>true</c>,
/// the intent's <see cref="DamageIntent.Amount"/> is replaced with
/// <c>ceil(Amount / 2)</c> — half the damage rounded up — modelling
/// "prevent half that damage, rounded up" oracle text without dropping
/// the intent on the floor.
///
/// Backs Gisela, Blade of Goldnight's defensive clause ("If a source
/// would deal damage to you or a permanent you control, prevent half
/// that damage, rounded up.") and any future card in that family
/// (Mercadia's Downhill Pony, etc.).
///
/// CR 615 prevents the damage that would be dealt — the engine models
/// this as a rewrite from <c>N</c> to <c>ceil(N/2)</c> rather than
/// publishing a separate <c>DamagePreventedEvent</c>, matching how the
/// rest of the shield family ships in v1 (PreventAllDamage* return
/// <c>null</c> to drop the intent; this one returns a smaller intent).
///
/// Rounding example: 5 damage → 3 (prevent 2 of 5, leaving 3).
/// 1 damage → 1 (prevent 0 of 1, leaving 1 — ceil(1/2) = 1).
/// 0 damage → short-circuits via the bus-side guard.
///
/// Per-effect dedup in <see cref="ReplacementBus.Apply{TIntent}"/>
/// (CR 616.1c) means halving fires at most once per intent per
/// registration. CR 616 ordering (player-choice when multiple
/// replacements apply) is deferred — registration order wins.
/// </summary>
public sealed class DamageHalveRoundedUpReplacement : IReplacementEffect<DamageIntent>
{
    private readonly Func<DamageIntent, bool> _predicate;

    /// <summary>
    /// Construct a halve-rounded-up prevention replacement gated by
    /// <paramref name="predicate"/>. Caller is responsible for any
    /// source / target / controller gates — the bus-side guard only
    /// short-circuits on <c>Amount &lt;= 0</c>.
    /// </summary>
    public DamageHalveRoundedUpReplacement(Func<DamageIntent, bool> predicate)
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
    {
        // ceil(amount / 2) — "prevent half that damage, rounded up"
        // leaves the rounded-up half behind.
        var halved = (intent.Amount + 1) / 2;
        return intent with { Amount = halved };
    }
}
