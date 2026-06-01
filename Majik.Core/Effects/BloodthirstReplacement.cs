using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.54 — Bloodthirst N. "If an opponent was dealt damage this turn, this
/// creature enters the battlefield with N +1/+1 counters on it."
///
/// <para>An ETB replacement (CR 614.1d / 702.54b) that watches the card's own
/// move onto the battlefield and, only when at least one of the card
/// controller's opponents has <see cref="Player.WasDealtDamageThisTurn"/> set,
/// stamps <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> += N. When no
/// opponent took damage this turn the replacement is inert and the creature
/// enters with no counters.</para>
///
/// <para>The bloodthirst condition is checked at the moment the creature would
/// enter (CR 702.54b — "as this creature enters"), so a burn spell earlier the
/// same turn that hit an opponent satisfies it regardless of intervening
/// life-gain (damage already happened; the per-turn flag latches).</para>
/// </summary>
public sealed class BloodthirstReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;
    private readonly int _amount;
    private readonly Func<IReadOnlyList<Player>> _opponents;

    /// <param name="card">The Bloodthirst creature.</param>
    /// <param name="amount">N — the number of +1/+1 counters granted when an
    /// opponent was dealt damage this turn.</param>
    /// <param name="opponents">Resolver for the card controller's opponents,
    /// checked for <see cref="Player.WasDealtDamageThisTurn"/> at ETB time.</param>
    public BloodthirstReplacement(ICard card, int amount, Func<IReadOnlyList<Player>> opponents)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Bloodthirst N ≥ 0.");
        _amount = amount;
        _opponents = opponents ?? throw new ArgumentNullException(nameof(opponents));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.ToZone == ZoneType.Battlefield
        && intent.FromZone != ZoneType.Battlefield
        && AnyOpponentDamaged();

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { PlusOneCountersOnEnter = intent.PlusOneCountersOnEnter + _amount };

    private bool AnyOpponentDamaged()
    {
        var opps = _opponents();
        if (opps == null) return false;
        foreach (var opp in opps)
        {
            if (opp != null && opp.WasDealtDamageThisTurn) return true;
        }
        return false;
    }
}
