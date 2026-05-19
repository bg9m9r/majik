using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5q — pairs of +1/+1 and -1/-1 counters cancel.</summary>
public sealed class CounterCancellationCheck : IStateBasedActionCheck
{
    public string Name => "CounterCancellation";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var perm in ctx.Cards.OfType<Permanent>())
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            var plus = perm.Counters.Count(CounterType.PlusOnePlusOne);
            var minus = perm.Counters.Count(CounterType.MinusOneMinusOne);
            var n = Math.Min(plus, minus);
            if (n > 0)
            {
                perm.Counters.Remove(CounterType.PlusOnePlusOne, n);
                perm.Counters.Remove(CounterType.MinusOneMinusOne, n);
                ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"{perm.Name}: {n} +1/+1 cancelled with {n} -1/-1"));
                anyExecuted = true;
            }
        }
        return anyExecuted;
    }
}
