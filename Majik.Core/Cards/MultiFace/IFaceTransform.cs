using Majik.Core.Cards;

namespace Majik.Core.Cards.MultiFace;

/// <summary>
/// Forward-looking contract for bistate face transforms — mechanics where
/// a card temporarily (or permanently, but reversibly via SBAs / other
/// abilities) sheds its printed face for an alternate one and back again.
///
/// Concrete plug-ins this contract is designed to fit (none implemented
/// in this PR; this ships only the contract + a Plot reference stub):
///
/// - <b>Plot</b> (CR 718) — exile from hand with a plot marker; while
///   plotted the card may be cast from exile as a sorcery without paying
///   its mana cost on a later turn. The transform <see cref="Apply"/>
///   stamps the plot marker + grants the cast-from-exile-as-sorcery
///   alt-cost; <see cref="Revert"/> clears them when the spell is cast.
/// - <b>Foretell</b> (CR 702.143) — same shape as Plot but with a
///   foretell-cost gate during <see cref="Apply"/>.
/// - <b>Manifest</b> / <b>Manifest dread</b> / <b>Disguise</b> — flip the
///   card face-down as a 2/2 creature; <see cref="Revert"/> turns it
///   face-up restoring its printed characteristics.
/// - <b>Transform</b> (CR 711) — flip front/back face. The existing
///   <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> covers this in
///   its current bistate form; a future MdfcFaceTransform adapter can
///   wrap it without changing the API surface MdfcState exposes today.
///
/// <para>
/// Lifecycle contract — implementations MUST satisfy:
/// </para>
/// <list type="number">
/// <item><see cref="Apply"/> is idempotent: calling Apply when
/// <see cref="IsActive"/> is already true is a no-op.</item>
/// <item><see cref="Revert"/> is idempotent: calling Revert when
/// <see cref="IsActive"/> is false is a no-op.</item>
/// <item><see cref="IsActive"/> reflects the current state observably —
/// it does not perform any mutation.</item>
/// <item>Transforms attach observable state to <see cref="ICard"/> via
/// their own backing field/property (e.g. <c>card.PlotMarker</c>); the
/// transform itself stays stateless so a single instance can drive
/// any number of cards that carry that mechanic.</item>
/// </list>
///
/// <para>
/// <b>Why this is bistate, not N-state.</b> Mechanics like Class
/// (CR 716) leveling have monotonic N-state progression with no revert
/// semantics — they are deliberately <em>not</em> face transforms and
/// keep their existing per-mechanic state primitives. Adventure
/// (CR 715) likewise is not a transform: the card itself never changes
/// face, the cast pipeline simply consults
/// <see cref="Majik.Core.CardData.Adventures.AdventureSpec"/> to compute
/// alt-cost + post-resolution exile.
/// </para>
/// </summary>
public interface IFaceTransform
{
    /// <summary>
    /// Human-readable identifier for the mechanic — e.g. "Plot",
    /// "Foretell", "Disguise", "Manifest", "Transform". Used by
    /// <see cref="MultiFaceCard"/> for active-transform lookup + by
    /// diagnostics / event payloads.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Mutate the card to enter its alternate face / state. MUST be
    /// idempotent — invoking Apply on a card that already has this
    /// transform active is a no-op.
    /// </summary>
    void Apply(ICard card, FaceContext ctx);

    /// <summary>
    /// Restore the card to its printed face / state. MUST be
    /// idempotent — invoking Revert on a card that does not currently
    /// have this transform active is a no-op. Transforms that are
    /// permanent (no in-game revert path) MAY throw
    /// <see cref="NotSupportedException"/> from this method, but MUST
    /// document that in their XML doc.
    /// </summary>
    void Revert(ICard card, FaceContext ctx);

    /// <summary>
    /// Query the current state. Pure observation — MUST NOT mutate
    /// the card. Implementations typically read a marker property on
    /// <paramref name="card"/> set by <see cref="Apply"/>.
    /// </summary>
    bool IsActive(ICard card);
}
