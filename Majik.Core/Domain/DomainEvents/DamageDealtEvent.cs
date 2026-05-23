using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Classifies the source of damage on a <see cref="DamageDealtEvent"/>.
/// Mirrors CR 119.1 (sources of damage) and 510 (combat damage step).
///
/// Frontend (portal) reads this to pick the right animation — combat
/// pings get a directional swipe from attacker to defender; spell pings
/// (Lightning Bolt) fly from the stack card to the target; ability
/// pings (Goblin Bombardment) fly from the activator on the battlefield.
/// </summary>
public enum DamageType
{
    Combat,
    Spell,
    Ability,
}

/// <summary>
/// Damage-dealt domain event with the per-source/target payload the
/// portal animations need (CR 119, CR 510). One event per source→target
/// pair per damage instance — combat lethal+non-lethal both emit (death
/// is a separate SBA event, CR 704.5g).
///
/// Subclassed by <see cref="CombatDamageDealtEvent"/> so existing
/// trigger bindings (`new EventTriggerCondition&lt;CombatDamageDealtEvent&gt;`)
/// continue to fire only on combat damage, while the wire payload
/// builder can match the parent and serialize a unified shape.
///
/// Source is always a card (creature for combat; spell/ability source
/// for non-combat). Target is either a card (creature / planeswalker)
/// or a player — <see cref="TargetIsPlayer"/> tells you which.
/// </summary>
public class DamageDealtEvent : GameEvent
{
    /// <summary>The card dealing damage (creature, instant, ability source).</summary>
    public ICard? SourceCard { get; }

    /// <summary>The player who controls the source (used when SourceCard is null).</summary>
    public Player? SourcePlayer { get; }

    /// <summary>Target card (creature or planeswalker), null when target is a player.</summary>
    public ICard? TargetCard { get; }

    /// <summary>Target player, null when target is a card.</summary>
    public Player? TargetPlayer { get; }

    /// <summary>Amount of damage dealt after replacement / prevention.</summary>
    public int Amount { get; }

    /// <summary>Kind of damage — Combat / Spell / Ability (CR 119.1).</summary>
    public DamageType DamageType { get; }

    /// <summary>Instance id of the source card, or the source player's id.
    /// Stable across the game so portal can correlate to its battlefield model.</summary>
    public Guid SourceInstanceId =>
        SourceCard?.InstanceId ?? SourcePlayer?.Id ?? Guid.Empty;

    /// <summary>Instance id of the target — card InstanceId for creatures /
    /// planeswalkers, Player.Id for player targets.</summary>
    public Guid TargetInstanceId =>
        TargetCard?.InstanceId ?? TargetPlayer?.Id ?? Guid.Empty;

    /// <summary>True when the target is a player (life loss); false when a card.</summary>
    public bool TargetIsPlayer => TargetPlayer != null;

    protected DamageDealtEvent(
        EventType eventType,
        ICard? sourceCard,
        Player? sourcePlayer,
        ICard? targetCard,
        Player? targetPlayer,
        int amount,
        DamageType damageType)
        : base(eventType)
    {
        if (sourceCard == null && sourcePlayer == null)
            throw new ArgumentException("Damage event requires a source card or source player.", nameof(sourceCard));
        if (targetCard == null && targetPlayer == null)
            throw new ArgumentException("Damage event requires a target card or target player.", nameof(targetCard));

        SourceCard = sourceCard;
        SourcePlayer = sourcePlayer;
        TargetCard = targetCard;
        TargetPlayer = targetPlayer;
        Amount = amount;
        DamageType = damageType;
    }

    public DamageDealtEvent(
        ICard? sourceCard,
        Player? sourcePlayer,
        ICard? targetCard,
        Player? targetPlayer,
        int amount,
        DamageType damageType)
        : this(EventType.DamageDealt, sourceCard, sourcePlayer, targetCard, targetPlayer, amount, damageType)
    {
    }
}
