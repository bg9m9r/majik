using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Remove a charge counter from &lt;source&gt;" — activation cost used by
/// abilities that consume charge counters (e.g. Umezawa's Jitte's three
/// modal abilities). Mirrors <see cref="RemovePlusOnePlusOneCounterCost"/>
/// for a different counter type. Implements <see cref="ICost"/> so it can
/// be attached directly to an <see cref="Majik.Core.Abilities.ActivatedAbility"/>.
/// </summary>
public sealed class RemoveChargeCounterCost : ICost
{
    private readonly Permanent _source;

    public int Amount { get; }

    public RemoveChargeCounterCost(Permanent source, int amount = 1)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        Amount = amount;
    }

    public string Description =>
        Amount == 1
            ? $"Remove a charge counter from {_source.Name}"
            : $"Remove {Amount} charge counters from {_source.Name}";

    public bool CanPay(Player player) =>
        _source.Counters.Count(CounterType.Charge) >= Amount;

    public void Pay(Player player)
    {
        if (!CanPay(player))
            throw new InvalidOperationException(
                $"Cannot pay counter cost: {_source.Name} has fewer than {Amount} charge counters.");
        _source.Counters.Remove(CounterType.Charge, Amount);
    }
}
