using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Remove a +1/+1 counter from &lt;source&gt;" — activation cost used by
/// abilities that consume counters (e.g. Walking Ballista's ping ability).
/// Implements <see cref="ICost"/> so it can be attached directly to an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/>.
/// </summary>
public sealed class RemovePlusOnePlusOneCounterCost : ICost, IRebindableCost
{
    private readonly Permanent _source;

    public int Amount { get; }

    public RemovePlusOnePlusOneCounterCost(Permanent source, int amount = 1)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        Amount = amount;
    }

    /// <summary>
    /// STAGE 1 (re-sourceable abilities) — re-home this counter cost onto a new
    /// source when the owning ability is re-sourced (CR 707.2). Swaps the
    /// captured source only when it is reference-equal to
    /// <paramref name="oldSource"/> and <paramref name="newSource"/> is a
    /// <see cref="Permanent"/>; otherwise returns this instance unchanged.
    /// </summary>
    public ICost RebindTo(object oldSource, object newSource) =>
        ReferenceEquals(_source, oldSource) && newSource is Permanent p
            ? new RemovePlusOnePlusOneCounterCost(p, Amount)
            : this;

    public string Description =>
        Amount == 1
            ? $"Remove a +1/+1 counter from {_source.Name}"
            : $"Remove {Amount} +1/+1 counters from {_source.Name}";

    public bool CanPay(Player player) =>
        _source.Counters.Count(CounterType.PlusOnePlusOne) >= Amount;

    public void Pay(Player player)
    {
        if (!CanPay(player))
            throw new InvalidOperationException(
                $"Cannot pay counter cost: {_source.Name} has fewer than {Amount} +1/+1 counters.");
        _source.Counters.Remove(CounterType.PlusOnePlusOne, Amount);
    }
}
