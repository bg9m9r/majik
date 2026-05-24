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
    ManaSpent,

    // Diagnostics — engine-meta events surfaced to the UI / logs without
    // being part of game-state changes. The vanilla-shell graceful-degrade
    // path uses this to tell the portal "the bot encountered an
    // unimplemented card; EV from here on is unreliable for that card".
    UnimplementedCardEncountered
}
