using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when combat damage is dealt (CR 510). Inherits
/// the per-creature/per-player payload exposed by
/// <see cref="DamageDealtEvent"/> (source/target InstanceIds,
/// targetIsPlayer, amount, damageType) for portal animation use; the
/// type itself remains a distinct subclass so existing trigger
/// bindings can keep pattern-matching it to fire only on combat
/// damage (Ragavan, Edric, etc).
/// </summary>
public class CombatDamageDealtEvent : DamageDealtEvent
{
    /// <summary>Combat damage is always dealt by a creature (CR 510.1).</summary>
    public Creature Source { get; }

    /// <summary>Target card (creature or planeswalker); null for player targets.</summary>
    public ICard? Target { get; }

    /// <summary>True when this damage was dealt in the first-strike sub-step (CR 702.7c).</summary>
    public bool IsFirstStrike { get; }

    public CombatDamageDealtEvent(Creature source, ICard? target, int amount, bool isFirstStrike = false)
        : base(
            EventType.CombatDamageDealt,
            sourceCard: source ?? throw new ArgumentNullException(nameof(source)),
            sourcePlayer: null,
            targetCard: target is Player ? null : target,
            targetPlayer: target as Player,
            amount: amount,
            damageType: DamageType.Combat)
    {
        Source = source;
        Target = target;
        IsFirstStrike = isFirstStrike;
    }

    public CombatDamageDealtEvent(Creature source, Player targetPlayer, int amount, bool isFirstStrike = false)
        : base(
            EventType.CombatDamageDealt,
            sourceCard: source ?? throw new ArgumentNullException(nameof(source)),
            sourcePlayer: null,
            targetCard: null,
            targetPlayer: targetPlayer ?? throw new ArgumentNullException(nameof(targetPlayer)),
            amount: amount,
            damageType: DamageType.Combat)
    {
        Source = source;
        Target = null;
        IsFirstStrike = isFirstStrike;
    }
}
