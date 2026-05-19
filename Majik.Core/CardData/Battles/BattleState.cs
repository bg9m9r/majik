using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;

namespace Majik.Core.CardData.Battles;

/// <summary>
/// CR 310 — Battle card. Enters the battlefield with N defense counters
/// (CR 310.5). A player or planeswalker designated as the protector
/// can be attacked the same way as the battle's controller. When the
/// battle has zero defense counters, SBA 704.5n puts it into its
/// owner's graveyard.
/// </summary>
public sealed class BattleState
{
    public Permanent Battle { get; }
    public Player? Protector { get; set; }
    private static readonly CounterType DefenseCounter = CounterType.Defense;

    public BattleState(Permanent battle, int initialDefense, Player? protector = null)
    {
        Battle = battle ?? throw new ArgumentNullException(nameof(battle));
        Protector = protector;
        if (initialDefense > 0) battle.Counters.Add(DefenseCounter, initialDefense);
    }

    public int DefenseCounters => Battle.Counters.Count(DefenseCounter);

    /// <summary>Combat damage to battle removes defense counters (CR 120.3c-ish for battles).</summary>
    public void TakeDamage(int amount)
    {
        if (amount <= 0) return;
        Battle.Counters.Remove(DefenseCounter, amount);
    }

    /// <summary>CR 704.5n — battle with 0 defense counters is put into graveyard.</summary>
    public bool ShouldBeSacrificed() => DefenseCounters == 0;
}
