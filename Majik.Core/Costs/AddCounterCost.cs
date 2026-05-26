using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.Costs;

/// <summary>
/// "Put a counter on &lt;source&gt;" — activation cost used by abilities
/// whose printed cost is the placement of a counter on the source
/// permanent (e.g. Devoted Druid's "Put a -1/-1 counter on Devoted
/// Druid: Untap Devoted Druid", charge-counter pump abilities).
///
/// CR 614.1 — counter placement caused by costs (CR 118.3) IS still a
/// "counter would be put on" event for replacement-effect purposes
/// (rulings on Vizier of Remedies + Devoted Druid confirm Vizier
/// replaces the cost-side -1/-1 counter). When a
/// <see cref="ReplacementBus"/> is supplied, the placement routes
/// through <see cref="CountersService.Add"/> so Vizier of Remedies /
/// Hardened Scales / Doubling Season can rewrite or cancel the cost
/// payload. When no bus is supplied the placement falls through to a
/// direct <see cref="Permanent.Counters"/> add — same posture as the
/// legacy direct-add factories.
///
/// Implements <see cref="ICost"/> so it can be attached directly to an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> alongside mana /
/// tap / sacrifice components.
/// </summary>
public sealed class AddCounterCost : ICost
{
    private readonly Permanent _source;
    private readonly ReplacementBus? _replacements;

    public CounterType CounterType { get; }
    public int Amount { get; }

    public AddCounterCost(
        Permanent source,
        CounterType counterType,
        int amount = 1,
        ReplacementBus? replacements = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        CounterType = counterType ?? throw new ArgumentNullException(nameof(counterType));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        Amount = amount;
        _replacements = replacements;
    }

    public string Description =>
        Amount == 1
            ? $"Put a {CounterType.Name} counter on {_source.Name}"
            : $"Put {Amount} {CounterType.Name} counters on {_source.Name}";

    /// <summary>
    /// Counter-placement costs are always payable on the source. The
    /// permanent's zone is NOT gated here — callers (ActionValidator,
    /// AbilityActivator) already gate activation on the source being on
    /// the battlefield via <see cref="Majik.Core.Abilities.ActivatedAbility"/>'s
    /// active-zones plumbing.
    /// </summary>
    public bool CanPay(Player player) => true;

    public void Pay(Player player)
    {
        if (_replacements != null)
        {
            CountersService.Add(_source, CounterType, Amount, _replacements);
        }
        else
        {
            _source.Counters.Add(CounterType, Amount);
        }
    }
}
