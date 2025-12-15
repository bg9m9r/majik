# Phase 2: Turn and Phase Management - Implementation Complete

## Overview

Phase 2 has been successfully implemented, providing complete turn and phase sequencing for the Magic: The Gathering game engine. The implementation builds upon the foundation from Phase 1 and the DDD refactoring from Phase 1.5.

## Completed Components

### ✅ 1. Turn and Phase Events

**Location**: `Majik.Core/Events/`

**Created Events**:
- `TurnStartedEvent`: Fired when a turn begins
- `TurnEndedEvent`: Fired when a turn ends
- `PhaseStartedEvent`: Fired when a phase begins
- `PhaseEndedEvent`: Fired when a phase ends
- `StepStartedEvent`: Fired when a step begins
- `StepEndedEvent`: Fired when a step ends
- `ExtraTurnAddedEvent`: Fired when an extra turn is added
- `ExtraPhaseAddedEvent`: Fired when an extra phase is added

**Features**:
- All events properly typed
- Include relevant context (player, turn number, phase type)
- Integrated with event bus

### ✅ 2. PhaseSequence Definition

**Location**: `Majik.Core/Game/PhaseSequence.cs`

**Features**:
- Standard phase sequence for normal turns
- First turn sequence (skips draw step)
- Helper methods for phase navigation
- Support for phase sequence queries

**Standard Sequence**:
1. Untap Step
2. Upkeep Step
3. Draw Step (skipped on first turn)
4. Main Phase (Pre-Combat)
5. Beginning of Combat Step
6. Declare Attackers Step
7. Declare Blockers Step
8. Combat Damage Step
9. End of Combat Step
10. Main Phase (Post-Combat)
11. End Step
12. Cleanup Step

### ✅ 3. TurnManager Implementation

**Location**: `Majik.Core/Game/TurnManager.cs`

**Key Features**:
- Turn order management (round-robin)
- Extra turn queue (FIFO)
- Turn number tracking
- First turn detection
- Active player management

**Key Methods**:
- `StartTurn(Player)`: Start a turn for a player
- `EndTurn()`: End the current turn
- `StartNextTurn()`: Start the next turn in sequence
- `AddExtraTurn(Player)`: Queue an extra turn
- `InitializeFirstTurn()`: Initialize the first turn

**Capabilities**:
- ✅ Handles turn rotation through all players
- ✅ Supports extra turns (queue-based)
- ✅ Tracks turn number
- ✅ Detects first turn (for draw step skipping)
- ✅ Publishes turn events

### ✅ 4. PhaseManager Implementation

**Location**: `Majik.Core/Game/PhaseManager.cs`

**Key Features**:
- Phase sequence management
- Phase transitions
- Extra phase queue
- Phase skipping support
- Turn completion detection

**Key Methods**:
- `InitializeForTurn(Player, bool)`: Initialize for a new turn
- `StartFirstPhase()`: Start the first phase
- `TransitionToNextPhase()`: Move to next phase
- `SkipCurrentPhase()`: Skip the current phase
- `AddExtraPhase(PhaseStateType)`: Add an extra phase
- `AddExtraCombatPhase()`: Add extra combat phases
- `AddExtraMainPhase()`: Add extra main phase
- `IsTurnComplete()`: Check if turn is finished

**Capabilities**:
- ✅ Manages standard phase sequence
- ✅ Supports extra phases (queue-based)
- ✅ Handles first turn (skips draw)
- ✅ Validates phase transitions
- ✅ Publishes phase events

### ✅ 5. Game Integration

**Location**: `Majik.Core/Domain/Aggregates/Game.cs`

**Integration Points**:
- TurnManager initialized after players added
- PhaseManager integrated with Game
- Turn and phase state machines connected
- `AdvancePhase()` method for phase progression
- `AdvanceTurn()` method for turn progression
- `ProcessTurnCycle()` method for complete turn

**New Methods**:
- `AdvancePhase()`: Move to next phase
- `AdvanceTurn()`: Move to next turn
- `ProcessTurnCycle()`: Process all phases in a turn

### ✅ 6. Phase Behaviors

**Location**: `Majik.Core/Game/Phases/`

**Implemented Phases**:
- `UntapStep.cs`: Untap step (placeholder for future untap logic)
- `DrawStep.cs`: Draw step (with draw card method)
- `MainPhase.cs`: Main phase (placeholder for spell casting)
- `EndStep.cs`: End step (placeholder for end step triggers)
- `CleanupStep.cs`: Cleanup step (with discard to hand size method)

**Note**: Phase behaviors are basic implementations that can be extended in future phases when we have:
- Permanents to untap
- Cards in library to draw
- Stack/priority for main phase
- Triggers for end step
- Hand size limits for cleanup

## Test Results

The console application successfully demonstrates:

### Turn Cycle
- ✅ First turn correctly skips draw step
- ✅ Subsequent turns include draw step
- ✅ All phases execute in correct order
- ✅ Turn transitions work correctly

### Extra Turns
- ✅ Extra turns can be added
- ✅ Extra turns execute in queue order
- ✅ Turn events fire correctly

### Extra Phases
- ✅ Extra combat phases can be added
- ✅ Extra phases execute correctly
- ✅ Phase events fire correctly

### Event System
- ✅ All turn events fire
- ✅ All phase events fire
- ✅ Events include correct context

**Sample Output**:
```
=== Majik Game Engine - Phase 2: Turn & Phase Management ===

[Event] Turn 1 started - Alice's turn
  → Phase: Untap
  → Phase: Upkeep
  → Phase: Main
  → Phase: BeginningOfCombat
  → Phase: DeclareAttackers
  → Phase: DeclareBlockers
  → Phase: CombatDamage
  → Phase: EndOfCombat
  → Phase: Main
  → Phase: End
  → Phase: Cleanup
[Event] Turn 1 ended - Alice's turn

[Event] Turn 2 started - Bob's turn
  → Phase: Untap
  → Phase: Upkeep
  → Phase: Draw  ← Draw step included on turn 2
```

## Architecture Improvements

### Before Phase 2
- ❌ No turn management
- ❌ No phase sequencing
- ❌ State machines registered but not active
- ❌ No turn/phase events

### After Phase 2
- ✅ Complete turn management
- ✅ Full phase sequencing
- ✅ Active turn/phase state machines
- ✅ Comprehensive turn/phase events
- ✅ Extra turns/phases support
- ✅ First turn special handling

## Files Created

### Core Components (8 files)
- `Game/TurnManager.cs`
- `Game/PhaseManager.cs`
- `Game/PhaseSequence.cs`
- `Game/Phases/UntapStep.cs`
- `Game/Phases/DrawStep.cs`
- `Game/Phases/MainPhase.cs`
- `Game/Phases/EndStep.cs`
- `Game/Phases/CleanupStep.cs`

### Events (8 files)
- `Events/TurnStartedEvent.cs`
- `Events/TurnEndedEvent.cs`
- `Events/PhaseStartedEvent.cs`
- `Events/PhaseEndedEvent.cs`
- `Events/StepStartedEvent.cs`
- `Events/StepEndedEvent.cs`
- `Events/ExtraTurnAddedEvent.cs`
- `Events/ExtraPhaseAddedEvent.cs`

**Total**: 16 new files created

## Build Status

✅ **All code compiles successfully with 0 errors and 0 warnings**

## Success Criteria Met

### Functional Requirements
- ✅ Complete turn cycle executes automatically
- ✅ All standard phases work correctly
- ✅ Turn order rotates correctly
- ✅ Extra turns can be added and execute
- ✅ Extra phases can be inserted
- ✅ All phase events fire correctly
- ✅ First turn skips draw step
- ✅ Cleanup step placeholder ready

### Technical Requirements
- ✅ Code follows DDD patterns from Phase 1.5
- ✅ All code compiles with 0 errors/warnings
- ✅ Console app demonstrates full turn cycle
- ✅ Events are properly typed and documented
- ✅ Services are testable and well-structured

### Testing Requirements
- ✅ Console app shows complete turn progression
- ✅ Events fire for all phase/turn changes
- ✅ Extra turns execute correctly
- ✅ Extra phases insert correctly
- ✅ Turn order rotates through all players

## Key Design Decisions

### 1. Lazy TurnManager Initialization
**Decision**: Initialize TurnManager after players are added
**Rationale**: TurnManager requires at least 2 players, so it can't be created in constructor

### 2. Queue-Based Extra Turns/Phases
**Decision**: Use FIFO queues for extra turns and phases
**Rationale**: Matches Magic rules, simple to implement, clear execution order

### 3. Phase Sequence Definition
**Decision**: Define sequences as static arrays with helper methods
**Rationale**: Simple, clear, easy to modify, supports first turn variation

### 4. Basic Phase Behaviors
**Decision**: Create placeholder phase classes with basic structure
**Rationale**: Establishes pattern, can be extended later when we have more game mechanics

## Next Steps (Phase 3)

After completing Phase 2, we're ready for Phase 3: Stack and Priority Management. This will add:
- Stack for spells and abilities
- Priority passing system
- Ability to cast spells during main phase
- Stack resolution

The turn and phase management from Phase 2 provides the foundation for these features.

## Summary

Phase 2 successfully implements complete turn and phase sequencing for the Magic: The Gathering game engine. The system:
- Manages turn order and transitions
- Sequences all standard phases
- Supports extra turns and phases
- Publishes comprehensive events
- Handles first turn special case
- Integrates cleanly with existing architecture

The implementation follows DDD principles, maintains code quality, and provides a solid foundation for future phases.
