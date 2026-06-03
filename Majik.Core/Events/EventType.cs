namespace Majik.Core.Events;

/// <summary>
/// Enumeration of all event types in the game.
/// </summary>
public enum EventType
{
    // Game Events
    GameStarted,
    GameEnded,
    GameStateChanged,
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

    // CR 701.40 — a permanent (creature) explored (revealed the top card
    // of its controller's library; land → hand, otherwise +1/+1 counter +
    // back-or-graveyard for the revealed card). The hook "Whenever a
    // creature you control explores" triggers (Wildgrowth Walker) subscribe
    // to this. Published after the explore action fully resolves.
    Explored,

    // CR 701.21 — a permanent became tapped. Published once per real tap at
    // every tap site (tap cost, "tap target …" effect, attack tap CR 508.1f,
    // manual Tap()). The "Whenever you tap a creature …" hook (Solitary
    // Sanctuary) subscribes here; the event's CausedBy carries the tapping
    // player so a "you tap" trigger can scope to its own controller.
    Tapped,

    // CR 122 / CR 614 — one or more counters have been placed on a PLAYER
    // (poison — CR 704.5c; energy — CR 107.16; experience — CR 107.14; or
    // a generic player counter), after all replacement effects have been
    // applied. The player-scoped twin of <c>CounterAdded</c>. Published by
    // <c>PlayerCountersService.Add</c> when a non-zero placement landed.
    PlayerCounterAdded,

    // CR 701.16 — a permanent was SACRIFICED (moved from the battlefield to
    // its owner's graveyard as a sacrifice cost or effect — Annihilator,
    // edicts, sac-costs). Distinct from a "destroy" / SBA death: carries the
    // sacrificing player and whether the permanent was a token, so
    // "whenever a/an [opponent] sacrifices …" aristocrat triggers
    // (It That Betrays, Mayhem Devil, Writhing Chrysalis) fire without the
    // CardMovedEvent over/under-fire footprint. Published by the
    // bus-aware <c>Fx.Sacrifice</c> overload at the moment the permanent
    // leaves the battlefield.
    PermanentSacrificed,
}
