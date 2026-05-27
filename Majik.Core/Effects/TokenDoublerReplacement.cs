using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614 — generic "double a token-creation intent" replacement. Wraps
/// a caller-supplied predicate over <see cref="TokenCreationIntent"/>;
/// when the predicate returns <c>true</c>, the intent is rewritten with
/// twice the original <see cref="TokenCreationIntent.Count"/>. Backs the
/// token-doubling pillar:
///
///   - <b>Doubling Season</b> — predicate gates on controller-match
///     (Doubling Season's controller). Also doubles counters via the
///     companion <c>CounterAddIntent</c> replacement registered by the
///     factory; this class only covers the token-creation half.
///   - <b>Parallel Lives</b> — predicate gates on controller-match;
///     creatures-only? Printed text says "creates twice that many of
///     those tokens"; no creature filter on the modern reprint, so the
///     factory uses an unfiltered controller-match (matches Comp Rules
///     errata + tournament rulings).
///   - <b>Anointed Procession</b> — same shape as Parallel Lives but
///     colour-shifted (White).
///
/// Per-effect dedup in <see cref="ReplacementBus.Apply{TIntent}"/>
/// (CR 616.1c) lets multiple instances stack multiplicatively: two
/// copies of Parallel Lives shipped-1 = 4 tokens (1 → 2 → 4); Parallel
/// Lives + Anointed Procession shipped-1 = 4 tokens (1 → 2 → 4); each
/// doubler fires once per intent regardless of source.
///
/// The effect is non-OneShot (sticks around for repeated firings while
/// the source permanent is on the battlefield) and uses
/// <see cref="object"/> identity (`this`) for the dedup tag, so multiple
/// independent registrations from distinct card sources each fire once
/// per intent.
/// </summary>
public sealed class TokenDoublerReplacement : IReplacementEffect<TokenCreationIntent>
{
    private readonly Func<TokenCreationIntent, bool> _predicate;

    /// <summary>
    /// Construct a doubling replacement gated by <paramref name="predicate"/>.
    /// Caller is responsible for any controller / token-type / source-zone
    /// gates — the bus-side guard only short-circuits on
    /// <c>Count &lt;= 0</c>.
    /// </summary>
    public TokenDoublerReplacement(Func<TokenCreationIntent, bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(TokenCreationIntent intent, IReadOnlyList<object> history)
    {
        if (intent.Count <= 0) return false;
        return _predicate(intent);
    }

    public TokenCreationIntent? Replace(TokenCreationIntent intent, IReadOnlyList<object> history)
        => intent with { Count = intent.Count * 2 };

    /// <summary>
    /// Convenience factory for the most common shape: "tokens you create
    /// are doubled" — gates only on <see cref="TokenCreationIntent.Controller"/>
    /// matching the supplied <paramref name="controller"/>. The source
    /// permanent's zone check is the caller's responsibility (factories
    /// either deregister on LTB or attach a zone predicate themselves;
    /// today every doubler factory registers once on Create and the
    /// retrofit relies on the source enchantment sitting on the
    /// battlefield — same gap as every other replacement today).
    /// </summary>
    public static TokenDoublerReplacement ForController(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return new TokenDoublerReplacement(intent => ReferenceEquals(intent.Controller, controller));
    }
}
