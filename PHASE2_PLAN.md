# Phase 2: Turn and Phase Management - Implementation Plan

## Overview

Phase 2 focuses on implementing complete turn and phase sequencing for the Magic: The Gathering game engine. This phase will build upon the foundation established in Phase 1 and the DDD refactoring from Phase 1.5 to create a fully functional turn cycle with all standard phases and steps.

## Goals

1. **Complete Turn Cycle**: Implement a full turn sequence from untap to cleanup
2. **Phase Management**: Create a system that manages all standard phases and steps
3. **Turn Sequencing**: Handle turn order, extra turns, and turn transitions
4. **Phase Transitions**: Automatic progression through phases with proper validation
5. **Dynamic Phase Insertion**: Support for adding extra phases (e.g., extra combat phases)
6. **Phase-Based Events**: Comprehensive events for phase/turn changes

## Current State Analysis

### What We Have (Phase 1 & 1.5)
- ✅ Hierarchical state machine (Game → Turn → Phase levels)
- ✅ State machine infrastructure (`StateMachine<T>`, `IState`)
- ✅ Turn and Phase state machines (registered but not active)
- ✅ All phase types defined (`PhaseStateType`, `TurnStateType`)
- ✅ Basic event system
- ✅ Game aggregate root
- ✅ Domain services foundation

### What's Missing
- ❌ TurnManager to coordinate turns
- ❌ PhaseManager to coordinate phases
- ❌ Active turn/phase state machines in Game
- ❌ Phase transition logic
- ❌ Turn sequencing logic
- ❌ Phase-specific behaviors (untap, draw, etc.)
- ❌ Support for extra turns/phases
- ❌ Integration with Game aggregate

## Implementation Tasks

### Task 1: TurnManager Implementation

**Location**: `Majik.Core/Game/TurnManager.cs`

**Purpose**: Manages turn order, turn transitions, and extra turns

**Key Responsibilities**:
- Track current turn number
- Manage active player
- Handle turn order (round-robin)
- Support extra turns (queue-based)
- Track turn history
- Coordinate with state machines

**Key Methods**:
```csharp
public class TurnManager
{
    public Player ActivePlayer { get; }
    public int TurnNumber { get; }
    public void StartTurn(Player player);
    public void EndTurn();
    public void AddExtraTurn(Player player);
    public Player GetNextPlayer();
    public bool HasExtraTurns();
}
```

**Features**:
- Turn queue for extra turns
- Turn priority order tracking
- Turn-based event publishing
- Integration with Game aggregate

**Domain Events**:
- `TurnStartedEvent`: Fired when a turn begins
- `TurnEndedEvent`: Fired when a turn ends
- `ExtraTurnAddedEvent`: Fired when an extra turn is added

### Task 2: PhaseManager Implementation

**Location**: `Majik.Core/Game/PhaseManager.cs`

**Purpose**: Manages phase sequence and transitions within a turn

**Key Responsibilities**:
- Manage current phase
- Handle phase transitions
- Support phase skipping
- Support extra phases (e.g., extra combat)
- Coordinate with PhaseStateMachine
- Validate phase transitions

**Key Methods**:
```csharp
public class PhaseManager
{
    public PhaseStateType CurrentPhase { get; }
    public void TransitionToNextPhase();
    public void SkipPhase(PhaseStateType phase);
    public void AddExtraPhase(PhaseStateType phase, int position);
    public bool CanTransition();
    public void ResetForNewTurn();
}
```

**Features**:
- Phase queue for extra phases
- Phase sequence definition
- Phase skip logic
- Phase transition validation
- Integration with TurnManager

**Domain Events**:
- `PhaseStartedEvent`: Fired when a phase begins
- `PhaseEndedEvent`: Fired when a phase ends
- `StepStartedEvent`: Fired when a step begins
- `StepEndedEvent`: Fired when a step ends

### Task 3: Phase Behaviors Implementation

**Location**: `Majik.Core/Game/Phases/`

**Purpose**: Implement specific behaviors for each phase/step

**Phases to Implement**:

#### 3.1 Untap Step
- **File**: `UntapStep.cs`
- **Behavior**: Untap all permanents controlled by active player
- **Events**: `UntapStepStartedEvent`, `CardUntappedEvent`, `UntapStepEndedEvent`

#### 3.2 Upkeep Step
- **File**: `UpkeepStep.cs`
- **Behavior**: Trigger upkeep triggers, handle upkeep costs
- **Events**: `UpkeepStepStartedEvent`, `UpkeepStepEndedEvent`

#### 3.3 Draw Step
- **File**: `DrawStep.cs`
- **Behavior**: Active player draws a card
- **Events**: `DrawStepStartedEvent`, `CardDrawnEvent`, `DrawStepEndedEvent`
- **Integration**: Use ZoneService to move card from library to hand

#### 3.4 Main Phase
- **File**: `MainPhase.cs`
- **Behavior**: Players can cast spells, activate abilities, play lands
- **Events**: `MainPhaseStartedEvent`, `MainPhaseEndedEvent`
- **Note**: Priority passing will be handled in Phase 3

#### 3.5 Combat Phase
- **File**: `CombatPhase.cs`
- **Behavior**: Coordinate combat steps (full combat in Phase 5)
- **Events**: `CombatPhaseStartedEvent`, `CombatPhaseEndedEvent`
- **Sub-steps**: Beginning of Combat, Declare Attackers, Declare Blockers, Combat Damage, End of Combat

#### 3.6 End Step
- **File**: `EndStep.cs`
- **Behavior**: Trigger end step triggers
- **Events**: `EndStepStartedEvent`, `EndStepEndedEvent`

#### 3.7 Cleanup Step
- **File**: `CleanupStep.cs`
- **Behavior**: Discard to hand size, remove damage, end turn
- **Events**: `CleanupStepStartedEvent`, `CleanupStepEndedEvent`

**Pattern**: Each phase/step should:
- Inherit from or use `PhaseState`/`TurnState`
- Have `OnEnter()` and `OnExit()` methods
- Publish appropriate events
- Integrate with domain services

### Task 4: Game Integration

**Location**: `Majik.Core/Domain/Aggregates/Game.cs`

**Purpose**: Integrate TurnManager and PhaseManager into Game aggregate

**Changes**:
- Add `TurnManager` and `PhaseManager` to Game
- Initialize state machines for turns and phases
- Coordinate turn/phase transitions
- Update `StartGame()` to begin first turn
- Add `AdvanceTurn()` and `AdvancePhase()` methods
- Integrate with existing state machines

**Key Updates**:
```csharp
public class Game
{
    private readonly TurnManager _turnManager;
    private readonly PhaseManager _phaseManager;
    private readonly TurnStateMachine _turnStateMachine;
    private readonly PhaseStateMachine _phaseStateMachine;
    
    public void AdvanceTurn();
    public void AdvancePhase();
    public void ProcessTurnCycle();
}
```

### Task 5: Phase Sequence Definition

**Location**: `Majik.Core/Game/PhaseSequence.cs`

**Purpose**: Define the standard phase sequence

**Implementation**:
- Define standard phase order
- Support for phase variations (e.g., first turn has no draw)
- Support for extra phases
- Phase sequence validation

**Standard Sequence**:
1. Untap Step
2. Upkeep Step
3. Draw Step
4. Main Phase (Pre-Combat)
5. Beginning of Combat Step
6. Declare Attackers Step
7. Declare Blockers Step
8. Combat Damage Step
9. End of Combat Step
10. Main Phase (Post-Combat)
11. End Step
12. Cleanup Step

**Special Cases**:
- First turn: Skip draw step
- Extra combat: Insert additional combat phases
- Skipped phases: Handle phase skipping

### Task 6: Dynamic Phase Insertion

**Location**: `Majik.Core/Game/PhaseManager.cs` (extension)

**Purpose**: Support adding extra phases dynamically

**Features**:
- Queue-based extra phase system
- Position-based insertion (before/after specific phase)
- Support for multiple extra phases
- Cleanup after extra phases complete

**Example Use Cases**:
- Extra combat phase (Aggravated Assault)
- Additional main phase
- Custom phases from card abilities

**API**:
```csharp
public void AddExtraPhase(PhaseStateType phase, PhaseInsertionPoint position);
public void AddExtraCombatPhase();
public void AddExtraMainPhase();
```

### Task 7: Turn and Phase Events

**Location**: `Majik.Core/Events/`

**Purpose**: Create comprehensive events for turn/phase changes

**New Events**:

#### Turn Events
- `TurnStartedEvent`: Turn begins
  - Properties: `Player`, `TurnNumber`, `Timestamp`
- `TurnEndedEvent`: Turn ends
  - Properties: `Player`, `TurnNumber`, `Timestamp`

#### Phase Events
- `PhaseStartedEvent`: Phase begins
  - Properties: `PhaseType`, `Player`, `Timestamp`
- `PhaseEndedEvent`: Phase ends
  - Properties: `PhaseType`, `Player`, `Timestamp`
- `StepStartedEvent`: Step begins
  - Properties: `StepType`, `Player`, `Timestamp`
- `StepEndedEvent`: Step ends
  - Properties: `StepType`, `Player`, `Timestamp`

#### Special Events
- `ExtraTurnAddedEvent`: Extra turn queued
- `ExtraPhaseAddedEvent`: Extra phase queued
- `PhaseSkippedEvent`: Phase was skipped

### Task 8: Domain Service Updates

**Location**: `Majik.Core/Services/`

**Purpose**: Update domain services to support turn/phase management

#### 8.1 GameService Updates
- Add turn management methods
- Add phase management methods
- Coordinate turn/phase transitions
- Handle game lifecycle

#### 8.2 New TurnService (Optional)
- Extract turn-specific operations
- Handle turn-based effects
- Manage turn history

## Implementation Order

### Step 1: Core Infrastructure (Day 1)
1. Create `TurnManager` class
2. Create `PhaseManager` class
3. Define phase sequence
4. Create basic turn/phase events

### Step 2: Integration (Day 2)
1. Integrate TurnManager into Game
2. Integrate PhaseManager into Game
3. Connect state machines
4. Update Game.StartGame()

### Step 3: Phase Behaviors (Day 3)
1. Implement Untap step
2. Implement Draw step
3. Implement Main phase
4. Implement End step
5. Implement Cleanup step

### Step 4: Turn Sequencing (Day 4)
1. Implement turn transitions
2. Implement turn order
3. Add extra turn support
4. Test full turn cycle

### Step 5: Advanced Features (Day 5)
1. Dynamic phase insertion
2. Phase skipping
3. First turn special handling
4. Comprehensive event publishing

### Step 6: Testing & Validation (Day 6)
1. Update console app
2. Test complete turn cycle
3. Test extra turns
4. Test extra phases
5. Verify all events fire correctly

## Technical Design Decisions

### 1. State Machine Integration
**Decision**: Use nested state machines (Game → Turn → Phase)
**Rationale**: 
- Matches Magic's hierarchical structure
- Clean separation of concerns
- Easy to extend

### 2. Turn/Phase Queue System
**Decision**: Use queues for extra turns/phases
**Rationale**:
- FIFO order matches Magic rules
- Easy to add/remove
- Clear execution order

### 3. Phase Sequence Definition
**Decision**: Define sequence as ordered list with insertion points
**Rationale**:
- Flexible for extra phases
- Easy to modify
- Clear phase order

### 4. Event-Driven Phase Transitions
**Decision**: Use events to trigger phase transitions
**Rationale**:
- Decoupled design
- Easy to extend
- UI can react to events

### 5. Domain Service Pattern
**Decision**: Use services for complex operations
**Rationale**:
- Follows DDD patterns from Phase 1.5
- Testable
- Single responsibility

## File Structure

```
Majik.Core/
├── Game/
│   ├── TurnManager.cs          # Turn management
│   ├── PhaseManager.cs          # Phase management
│   ├── PhaseSequence.cs         # Phase sequence definition
│   └── Phases/                  # Phase implementations
│       ├── UntapStep.cs
│       ├── UpkeepStep.cs
│       ├── DrawStep.cs
│       ├── MainPhase.cs
│       ├── CombatPhase.cs
│       ├── EndStep.cs
│       └── CleanupStep.cs
├── Events/                      # New events
│   ├── TurnStartedEvent.cs
│   ├── TurnEndedEvent.cs
│   ├── PhaseStartedEvent.cs
│   ├── PhaseEndedEvent.cs
│   ├── StepStartedEvent.cs
│   ├── StepEndedEvent.cs
│   ├── ExtraTurnAddedEvent.cs
│   └── ExtraPhaseAddedEvent.cs
└── Domain/Aggregates/
    └── Game.cs                  # Updated with turn/phase management
```

## Success Criteria

### Functional Requirements
- ✅ Complete turn cycle executes automatically
- ✅ All standard phases work correctly
- ✅ Turn order rotates correctly
- ✅ Extra turns can be added and execute
- ✅ Extra phases can be inserted
- ✅ All phase events fire correctly
- ✅ First turn skips draw step
- ✅ Cleanup step discards to hand size

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

## Dependencies

### From Phase 1
- State machine infrastructure
- Event system
- Zone system
- Card system

### From Phase 1.5
- Value objects
- Domain services
- Game aggregate
- Encapsulated entities

### For Phase 3 (Future)
- Stack system (for casting spells in main phase)
- Priority system (for passing priority in phases)

## Risks and Mitigations

### Risk 1: Complex Phase Transitions
**Mitigation**: Start simple, add complexity incrementally

### Risk 2: State Machine Complexity
**Mitigation**: Use existing state machine infrastructure, keep it simple

### Risk 3: Event Overhead
**Mitigation**: Events are lightweight, only publish what's needed

### Risk 4: Integration Issues
**Mitigation**: Integrate incrementally, test after each step

## Estimated Effort

- **TurnManager**: 4-6 hours
- **PhaseManager**: 4-6 hours
- **Phase Behaviors**: 6-8 hours
- **Game Integration**: 3-4 hours
- **Events**: 2-3 hours
- **Testing**: 3-4 hours

**Total**: ~22-31 hours (approximately 1 week)

## Deliverables

1. ✅ `TurnManager` class fully implemented
2. ✅ `PhaseManager` class fully implemented
3. ✅ All standard phases implemented
4. ✅ Game integrated with turn/phase management
5. ✅ Complete turn cycle working
6. ✅ Extra turns/phases supported
7. ✅ Comprehensive events for all transitions
8. ✅ Updated console app demonstrating full functionality
9. ✅ Documentation updated

## Next Steps After Phase 2

After completing Phase 2, we'll have:
- Complete turn and phase sequencing
- Foundation for Phase 3 (Stack and Priority)
- Ability to test turn-based mechanics
- Events ready for UI integration

Phase 3 will build on this to add:
- Stack for spells and abilities
- Priority passing system
- Ability to cast spells during main phase
- Stack resolution

## Notes

- This phase focuses on sequencing, not on complex game mechanics
- Combat details will be handled in Phase 5
- Stack and priority will be handled in Phase 3
- Card abilities will be handled in Phase 4
- Keep it simple and working, complexity can be added later
