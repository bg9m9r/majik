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
    LibraryShuffled,

    // Player Events
    LifeChanged,
    ManaAdded,
    ManaSpent,

    // CR 716 — Class enchantment leveled up (level-up activated ability
    // resolved, CurrentLevel incremented).
    ClassLeveledUp,

    // CR 702.131 — Ascend: player reached 10+ permanents and gained the
    // city's blessing for the rest of the game (latches on Player).
    GainedCitysBlessing,

    // Diagnostics — engine-meta events surfaced to the UI / logs without
    // being part of game-state changes. The vanilla-shell graceful-degrade
    // path uses this to tell the portal "the bot encountered an
    // unimplemented card; EV from here on is unreliable for that card".
    UnimplementedCardEncountered,

    // CR 702.62d — the last time counter on a suspended card has been
    // removed; the registry will cast the card for free immediately
    // after the event is published.
    SuspendCounterDrained,

    // CR 103.5 — opening-hand check. Fired once per player at game
    // start AFTER the initial draw + mulligan resolution but BEFORE
    // the first turn begins. Carries the opening-hand snapshot so
    // alt-cost surfaces (Leyline keyword, Gemstone Caverns, Chancellor
    // cycle) can prompt the player.
    OpeningHandCheck,

    // CR 702.32d — a card's Cycling activated ability has resolved
    // (cost paid + card discarded + replacement card drawn). The hook
    // "Whenever a player cycles a card" triggers (Lightning Rift,
    // Astral Slide, Decree of Justice, etc.) subscribe to.
    CardCycled,

    // CR 701.42 — a player surveiled (peeked top N, partitioned into
    // graveyard-bound and library-top-bound). The hook "Whenever you
    // surveil" / "Whenever ~ surveils" triggers (Ledger Shredder,
    // Glimpse the Unthinkable, etc.) subscribe to.
    Surveil,

    // CR 121 / CR 614 — one or more counters have been placed on a
    // permanent (after all replacement effects have been applied; the
    // event carries the actual amount committed). The hook "Whenever
    // one or more +1/+1 counters are put on a permanent you control"
    // (Animation Module, Hardened Scales rider tests, etc.) subscribes
    // to this — Hardened Scales itself is a REPLACEMENT effect that
    // rewrites the intent BEFORE commit, so it runs first and the
    // amount on this event already reflects its bump.
    CounterAdded,
}
