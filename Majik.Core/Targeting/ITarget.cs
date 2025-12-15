namespace Majik.Core.Targeting;

/// <summary>
/// Interface for objects that can be targeted by spells and abilities.
/// </summary>
public interface ITarget
{
    /// <summary>
    /// Unique identifier for this target.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The type of target (Player, Card, Permanent, Spell, Ability).
    /// </summary>
    TargetType TargetType { get; }
}

/// <summary>
/// Types of targets that can be chosen.
/// </summary>
public enum TargetType
{
    /// <summary>
    /// Target a player.
    /// </summary>
    Player,

    /// <summary>
    /// Target a card.
    /// </summary>
    Card,

    /// <summary>
    /// Target a permanent.
    /// </summary>
    Permanent,

    /// <summary>
    /// Target a spell on the stack.
    /// </summary>
    Spell,

    /// <summary>
    /// Target an ability on the stack.
    /// </summary>
    Ability
}
