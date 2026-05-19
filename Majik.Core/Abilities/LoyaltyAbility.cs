using Majik.Core.Cards;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 606 — a loyalty ability. The number printed in the cost box is
/// added to / subtracted from the planeswalker's loyalty as part of
/// activation cost. Only one loyalty ability may be activated per
/// planeswalker per turn (CR 606.5), tracked by
/// <see cref="Permanent.LoyaltyAbilityActivatedThisTurn"/>.
///
/// <see cref="LoyaltyChange"/>: positive = "+N" (add counters),
/// negative = "-N" (remove counters; activation illegal if not enough
/// loyalty), zero = "0:" abilities.
/// </summary>
public sealed class LoyaltyAbility : IAbility
{
    private readonly Action _effect;
    public Planeswalker Source { get; }
    public int LoyaltyChange { get; }

    public LoyaltyAbility(Planeswalker source, int loyaltyChange, Action effect)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        LoyaltyChange = loyaltyChange;
        _effect = effect ?? throw new ArgumentNullException(nameof(effect));
    }

    public string Description => LoyaltyChange switch
    {
        > 0 => $"+{LoyaltyChange}",
        < 0 => LoyaltyChange.ToString(),
        _ => "0",
    };

    public bool CanActivate()
    {
        if (Source.LoyaltyAbilityActivatedThisTurn) return false;
        if (LoyaltyChange < 0 && Source.Loyalty + LoyaltyChange < 0) return false;
        return true;
    }

    public void Activate()
    {
        if (!CanActivate())
            throw new InvalidOperationException("Loyalty ability cannot be activated");

        if (LoyaltyChange > 0) Source.AddLoyalty(LoyaltyChange);
        else if (LoyaltyChange < 0) Source.RemoveLoyalty(-LoyaltyChange);

        Source.LoyaltyAbilityActivatedThisTurn = true;
        _effect();
    }
}
