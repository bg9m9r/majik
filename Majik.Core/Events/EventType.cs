namespace Majik.Core.Events;

/// <summary>
/// Enumeration of all event types in the game.
/// </summary>
public enum EventType
{
    // Game Events
    GameStarted,
    GameEnded,
    TurnStarted,
    TurnEnded,

    // Phase Events
    PhaseStarted,
    PhaseEnded,
    StepStarted,
    StepEnded,

    // Card Events
    CardDrawn,
    CardPlayed,
    CardResolved,
    CardDestroyed,
    CardMoved,
    CardRevealed,

    // Combat Events
    CombatStarted,
    AttackersDeclared,
    BlockersDeclared,
    CombatDamageDealt,
    CombatEnded,
    DamageDealt,

    // Ability Events
    Triggered,
    TriggeredAbilityTriggered,
    Activated,
    Resolved,

    // Zone Events
    ZoneChanged,

    // Player Events
    LifeChanged,
    ManaAdded,
    ManaSpent
}
