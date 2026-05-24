using Majik.Core.Cards;

namespace Majik.Core.Players.Agents;

/// <summary>
/// How a player chose to pay a mana cost: the set of permanents whose mana
/// abilities will be activated (in order). Empty means "use only floating mana".
/// <para>
/// CR 601.2 / CR 727 — a remote player can also bail out of the cast while
/// the engine is at the cost-payment step. <see cref="IsCancelled"/> = true
/// signals "abort this cast"; the dispatch site refunds any partially-paid
/// mana, leaves the spell in hand, and surfaces no <c>SpellCastEvent</c>.
/// The <see cref="Cancelled"/> singleton is the canonical sentinel returned
/// by <c>RemoteAgent.Submit</c> when a <c>CancelCastCommand</c> resolves the
/// <c>ChooseManaSourcesAsync</c> prompt.
/// </para>
/// </summary>
public sealed record ManaPayment(IReadOnlyList<ICard> Sources)
{
    public static readonly ManaPayment Empty = new(Array.Empty<ICard>());

    /// <summary>
    /// Sentinel returned when the player aborts the cast at cost-payment
    /// time. Carries an empty source list. Cost-payment sites check
    /// <see cref="IsCancelled"/> and skip casting (and refund any pool
    /// mana already deducted earlier in the dispatch).
    /// </summary>
    public static readonly ManaPayment Cancelled = new(Array.Empty<ICard>())
    {
        IsCancelled = true,
    };

    /// <summary>
    /// True when this payment is the <see cref="Cancelled"/> sentinel.
    /// Defaults to false for any payment that names real sources.
    /// </summary>
    public bool IsCancelled { get; init; }
}
