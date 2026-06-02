using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.104a (Revolt) + CR 614.1d — "Revolt — This creature enters with a
/// +1/+1 counter on it if a permanent left the battlefield under your control
/// this turn."
///
/// <para>An ETB replacement that watches the card's own move onto the
/// battlefield and, only when Revolt is active for the card's controller at
/// the moment it would enter, stamps
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> += N. When no permanent
/// the controller controlled left the battlefield this turn, the replacement
/// is inert and the creature enters with no counters.</para>
///
/// <para>The Revolt condition is checked as the creature would enter (CR 614.1d
/// — the replacement applies to the entering event itself). Whether Revolt is
/// active is supplied by the caller via a <c>revoltActive</c> predicate, which
/// typically reads <see cref="Game.TurnState.RevoltActive"/> for the card's
/// controller. A null-returning / false predicate (no
/// <see cref="Game.TurnState"/> wired — shape / dispatcher tests) leaves the
/// replacement inert, so the creature enters vanilla.</para>
///
/// <para>Generalizes the conditional enters-with-counter shape used by
/// Bloodthirst (<see cref="BloodthirstReplacement"/>); the only difference is
/// the gating predicate (opponent-damaged vs. a-permanent-left-your-control).
/// Covers Narnam Renegade (N=1) and any future Revolt enters-with-counter
/// creatures.</para>
/// </summary>
public sealed class RevoltEntersWithCountersReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;
    private readonly int _amount;
    private readonly Func<bool> _revoltActive;

    /// <param name="card">The Revolt creature.</param>
    /// <param name="amount">N — the number of +1/+1 counters granted when
    /// Revolt is active (1 for Narnam Renegade).</param>
    /// <param name="revoltActive">Predicate evaluated as the creature would
    /// enter; true iff a permanent the card's controller controlled left the
    /// battlefield this turn (CR 702.104a).</param>
    public RevoltEntersWithCountersReplacement(ICard card, int amount, Func<bool> revoltActive)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Counter amount N ≥ 0.");
        _amount = amount;
        _revoltActive = revoltActive ?? throw new ArgumentNullException(nameof(revoltActive));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield
        && _revoltActive();

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { PlusOneCountersOnEnter = intent.PlusOneCountersOnEnter + _amount };
}
