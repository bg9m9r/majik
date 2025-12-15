using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when combat damage is dealt (Rule 510).
/// </summary>
public class CombatDamageDealtEvent : GameEvent
{
    public Creature Source { get; }
    public ICard? Target { get; } // Creature, Player, or Planeswalker
    public Player? TargetPlayer { get; }
    public int Amount { get; }
    public bool IsFirstStrike { get; }

    public CombatDamageDealtEvent(Creature source, ICard? target, int amount, bool isFirstStrike = false)
        : base(EventType.CombatDamageDealt)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target;
        TargetPlayer = target as Player;
        Amount = amount;
        IsFirstStrike = isFirstStrike;
    }

    public CombatDamageDealtEvent(Creature source, Player targetPlayer, int amount, bool isFirstStrike = false)
        : base(EventType.CombatDamageDealt)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = null;
        TargetPlayer = targetPlayer ?? throw new ArgumentNullException(nameof(targetPlayer));
        Amount = amount;
        IsFirstStrike = isFirstStrike;
    }
}
