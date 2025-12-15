# Majik - Magic: The Gathering Engine Implementation Plan

## Overview

This document outlines the comprehensive plan for implementing a Magic: The Gathering game engine in C#. The engine will be event-driven, use a hierarchical state machine architecture, and be designed for extensibility to handle complex game mechanics.

**⚠️ NOTE**: This is the original plan. For an updated status reflecting completed work, see `IMPLEMENTATION_PLAN_UPDATED.md`.

**Current Status**: 
- ✅ Phases 1, 1.5, 2, 2.5, 2.75, 3, 3.5: Complete
- 🟡 Phase 4: Partially complete (core done, some features remaining)
- ⏳ Phases 5, 6, 7, 8: Not started or partially started

## Core Design Principles

1. **Event-Driven Architecture**: All game actions emit events that external systems can subscribe to
2. **State Machine**: Hierarchical state machine manages game flow, turns, and phases
3. **Extensibility**: Support for dynamic phase insertion, extra turns, and complex abilities
4. **UI-Agnostic**: Engine has zero UI dependencies
5. **Composable**: Card abilities build from base mechanics
6. **Testable**: Clear separation of concerns enables unit testing

## Architecture Overview

### High-Level Components

```
Majik.Core (Engine)
├── StateMachine/          # State machine implementation
├── Events/                # Event system
├── Game/                  # Core game logic
│   ├── GameState.cs      # Overall game state
│   ├── TurnManager.cs    # Turn sequencing
│   ├── PhaseManager.cs   # Phase sequencing
│   └── PriorityManager.cs # Priority/stack management
├── Zones/                 # Zone management
├── Cards/                 # Card system
├── Players/               # Player management
├── Abilities/             # Ability system
└── Rules/                 # Rules engine
```

## Detailed Component Design

### 1. State Machine Architecture

#### 1.1 Hierarchical State Machine

The state machine will have three levels:

**Level 1: Game States**
- `Initializing`: Setting up the game
- `Mulligan`: Players deciding on opening hands
- `Playing`: Active gameplay
- `GameOver`: Game has ended

**Level 2: Turn States**
- `TurnBeginning`: Start of turn effects
- `PreCombatMain`: Main phase before combat
- `Combat`: Combat phase
- `PostCombatMain`: Main phase after combat
- `TurnEnding`: End of turn effects

**Level 3: Phase States**
- `Untap`: Untap step
- `Upkeep`: Upkeep step
- `Draw`: Draw step
- `Main`: Main phase
- `BeginningOfCombat`: Beginning of combat step
- `DeclareAttackers`: Declare attackers step
- `DeclareBlockers`: Declare blockers step
- `CombatDamage`: Combat damage step
- `EndOfCombat`: End of combat step
- `End`: End step
- `Cleanup`: Cleanup step

#### 1.2 State Machine Implementation

**Key Classes:**
- `IState`: Base interface for all states
- `StateMachine<T>`: Generic state machine
- `GameStateMachine`: Top-level game state machine
- `TurnStateMachine`: Turn-level state machine
- `PhaseStateMachine`: Phase-level state machine

**Features:**
- State entry/exit callbacks
- State transition validation
- Nested state machines
- State history for undo/redo (future)
- Dynamic state insertion (for extra phases/turns)

### 2. Event System

#### 2.1 Event Architecture

**Base Event Class:**
```csharp
public abstract class GameEvent
{
    public DateTime Timestamp { get; }
    public Guid EventId { get; }
    public EventType Type { get; }
}
```

**Event Categories:**
- **Game Events**: `GameStarted`, `GameEnded`, `TurnStarted`, `TurnEnded`
- **Phase Events**: `PhaseStarted`, `PhaseEnded`, `StepStarted`, `StepEnded`
- **Card Events**: `CardDrawn`, `CardPlayed`, `CardResolved`, `CardDestroyed`
- **Combat Events**: `AttackersDeclared`, `BlockersDeclared`, `DamageDealt`
- **Ability Events**: `Triggered`, `Activated`, `Resolved`
- **Zone Events**: `CardMoved`, `ZoneChanged`
- **Player Events**: `LifeChanged`, `ManaAdded`, `ManaSpent`

#### 2.2 Event Bus

**Key Classes:**
- `IEventBus`: Event bus interface
- `EventBus`: Default implementation
- `IEventHandler<T>`: Generic event handler interface

**Features:**
- Type-safe event subscriptions
- Event filtering
- Async event handling
- Event replay (for debugging/testing)

### 3. Game State Management

#### 3.1 GameState Class

**Responsibilities:**
- Maintain overall game state
- Manage players
- Coordinate state machines
- Track game history
- Validate game rules

**Key Properties:**
- `Players`: List of players
- `ActivePlayer`: Current active player
- `TurnNumber`: Current turn number
- `StateMachine`: Game state machine
- `EventBus`: Event bus instance

#### 3.2 Turn Management

**TurnManager Class:**
- Manages turn order
- Handles extra turns
- Tracks turn history
- Manages turn-based effects

**Key Features:**
- Turn queue (for extra turns)
- Turn priority order
- Turn-based triggers

#### 3.3 Phase Management

**PhaseManager Class:**
- Manages phase sequence
- Handles dynamic phase insertion
- Tracks phase history
- Manages phase-based triggers

**Key Features:**
- Phase queue (for extra phases)
- Phase skip logic
- Phase-based triggers

### 4. Zone System

#### 4.1 Zone Types

- `Library`: Player's deck
- `Hand`: Player's hand
- `Battlefield`: In-play zone
- `Graveyard`: Discard pile
- `Exile`: Exiled cards
- `Stack`: Spell/ability stack
- `Command`: Command zone (for commanders)

#### 4.2 Zone Implementation

**Key Classes:**
- `IZone`: Base zone interface
- `Zone<T>`: Generic zone implementation
- `ZoneManager`: Manages all zones for a player

**Features:**
- Type-safe zone contents
- Zone change events
- Zone visibility rules
- Shuffle operations (for library)

### 5. Card System

#### 5.1 Card Base Classes

**Key Classes:**
- `ICard`: Base card interface
- `Card`: Base card implementation
- `Permanent`: Cards that stay on battlefield
- `Spell`: Cards on the stack
- `Creature`: Creature cards
- `Land`: Land cards
- `Instant`: Instant spells
- `Sorcery`: Sorcery spells
- `Enchantment`: Enchantment cards
- `Artifact`: Artifact cards
- `Planeswalker`: Planeswalker cards

#### 5.2 Card Properties

- `Name`: Card name
- `ManaCost`: Mana cost
- `Types`: Card types
- `Abilities`: List of abilities
- `Power`: Power (for creatures)
- `Toughness`: Toughness (for creatures)
- `Loyalty`: Loyalty (for planeswalkers)
- `Zone`: Current zone
- `Owner`: Card owner
- `Controller`: Current controller

### 6. Ability System

#### 6.1 Ability Types

- **Triggered Abilities**: Fire on events
- **Activated Abilities**: Player-activated
- **Static Abilities**: Continuous effects
- **Replacement Effects**: Modify events
- **Mana Abilities**: Generate mana

#### 6.2 Ability Implementation

**Key Classes:**
- `IAbility`: Base ability interface
- `TriggeredAbility`: Triggered ability implementation
- `ActivatedAbility`: Activated ability implementation
- `StaticAbility`: Static ability implementation
- `ReplacementEffect`: Replacement effect implementation
- `AbilityResolver`: Resolves abilities on the stack

**Features:**
- Ability targeting
- Ability costs
- Ability conditions
- Ability timing restrictions

### 7. Stack and Priority System

#### 7.1 Stack Management

**Stack Class:**
- Manages spell/ability resolution order
- LIFO (Last In, First Out) structure
- Tracks resolution state

**Features:**
- Add to stack
- Resolve top of stack
- Check stack empty
- Stack change events

#### 7.2 Priority System

**PriorityManager Class:**
- Manages priority passing
- Determines when players can act
- Handles priority holds

**Priority Rules:**
- Active player gets priority first
- Priority passes after each action
- Stack must be empty to move to next phase
- Players can hold priority

### 8. Combat System

#### 8.1 Combat Phases

1. **Beginning of Combat**: Triggers fire
2. **Declare Attackers**: Active player declares attackers
3. **Declare Blockers**: Defending player declares blockers
4. **Combat Damage**: Damage is dealt
5. **End of Combat**: End of combat triggers

#### 8.2 Combat Implementation

**Key Classes:**
- `CombatManager`: Manages combat
- `Combat`: Represents a combat instance
- `Attacker`: Attacking creature
- `Blocker`: Blocking creature
- `DamageAssignment`: Damage assignment

**Features:**
- Multiple attackers
- Multiple blockers per attacker
- Damage assignment order
- First strike/double strike
- Trample damage

### 9. Rules Engine

#### 9.1 Rules Validation

**RulesEngine Class:**
- Validates game actions
- Enforces game rules
- Checks legality of plays

**Key Rules:**
- Mana payment
- Timing restrictions
- Targeting rules
- Zone restrictions
- State-based actions

#### 9.2 State-Based Actions

**StateBasedActions Class:**
- Checks state-based actions
- Executes state-based actions
- Runs after each event

**Common State-Based Actions:**
- Creature dies (0 or less toughness)
- Planeswalker dies (0 loyalty)
- Player loses (0 or less life)
- Legend rule
- Planeswalker uniqueness rule

## Implementation Phases

### ✅ Phase 1: Foundation (COMPLETE)
**Status**: ✅ Fully implemented and tested
**Goal**: Basic structure and state machine

**Tasks:**
1. Create project structure (Majik.Core, Majik.Console)
2. Implement base event system
3. Implement hierarchical state machine
4. Create basic game state management
5. Implement zone system
6. Create basic card classes

**Deliverables:**
- Working state machine
- Event bus with subscriptions
- Basic game initialization
- Zone management

### ✅ Phase 2: Turn and Phase Management (COMPLETE)
**Status**: ✅ Fully implemented and tested
**Goal**: Complete turn/phase sequencing

**Tasks:**
1. Implement TurnManager
2. Implement PhaseManager
3. Create all standard phases/steps
4. Implement phase transitions
5. Add phase-based events
6. Support for dynamic phase insertion

**Deliverables:**
- Complete turn cycle
- All standard phases working
- Phase events firing
- Ability to insert extra phases

### ✅ Phase 3: Stack and Priority (COMPLETE)
**Status**: ✅ Fully implemented and tested
**Goal**: Stack resolution and priority system

**Tasks:**
1. Implement Stack class
2. Implement PriorityManager
3. Create priority passing logic
4. Implement stack resolution
5. Add stack events

**Deliverables:**
- Working stack
- Priority passing
- Stack resolution
- Players can cast spells

### 🟡 Phase 4: Card System and Abilities (PARTIALLY COMPLETE)
**Status**: 🟡 Core functionality complete, some features remaining
**Goal**: Complete card system with abilities

**✅ Completed Tasks:**
1. ✅ Complete card type hierarchy
2. ✅ Implement ability system (foundation)
3. ✅ Create triggered abilities (foundation)
4. ✅ Create activated abilities (full implementation)
5. ✅ Add ability targeting
6. ✅ Cost system
7. ✅ Spell casting with costs and targeting
8. ✅ Spell/ability resolution
9. ✅ State-based actions (foundation)

**⏳ Remaining Tasks:**
1. ⏳ Static abilities (full implementation)
2. ⏳ Replacement effects
3. ⏳ Mana abilities (full mana pool system)
4. ⏳ Trigger manager (event-driven triggers)
5. ⏳ Ability effects (effect execution system)

**Deliverables:**
- ✅ Complete card system
- ✅ Working activated abilities
- ✅ Ability resolution
- ✅ Targeting system
- ✅ Cost system
- ⏳ Full static abilities
- ⏳ Replacement effects
- ⏳ Full mana system

**See**: `PHASE4_PROGRESS.md` for detailed status

### ⏳ Phase 5: Combat System (NOT STARTED)
**Status**: ⏳ Not started
**Goal**: Full combat implementation

**Tasks:**
1. Implement CombatManager
2. Create combat phases
3. Implement attacker/blocker declaration
4. Implement damage calculation
5. Handle combat abilities (first strike, trample, etc.)

**Deliverables:**
- Working combat
- All combat phases
- Damage resolution
- Combat abilities

### 🟡 Phase 6: Rules Engine (PARTIALLY STARTED)
**Status**: 🟡 Foundation exists, needs enhancement
**Goal**: Rules validation and state-based actions

**✅ Completed:**
- ✅ StateBasedActions class (foundation)
- ✅ Basic SBA checking (player loses, creature dies, planeswalker dies)

**⏳ Remaining Tasks:**
1. ⏳ Implement RulesEngine (comprehensive validation)
2. ⏳ Expand state-based actions (Legend rule, Planeswalker uniqueness, etc.)
3. ⏳ Implement action validation
4. ⏳ Add comprehensive rule checking
5. ⏳ Create comprehensive rule tests
6. ⏳ Integrate SBA checking throughout game flow

**Deliverables:**
- 🟡 State-based actions (foundation)
- ⏳ Rules validation
- ⏳ Action legality checking
- ⏳ Comprehensive test coverage

### 🟡 Phase 7: Advanced Features (PARTIALLY COMPLETE)
**Status**: 🟡 Infrastructure complete, features need completion
**Goal**: Extra turns, complex abilities, edge cases

**✅ Completed:**
- ✅ Extra turns (implemented in TurnManager)
- ✅ Extra phases (implemented in PhaseManager)

**⏳ Remaining Tasks:**
1. ⏳ Handle complex triggered abilities
2. ⏳ Implement replacement effects (will be in Phase 4 completion)
3. ⏳ Add comprehensive edge case handling
4. ⏳ Complex card interactions
5. ⏳ Multiplayer support enhancements

**Deliverables:**
- ✅ Extra turns working
- ✅ Extra phases working
- ⏳ Complex abilities
- ⏳ Edge cases handled

### ⏳ Phase 8: Testing and Polish (NOT STARTED)
**Status**: ⏳ Not started
**Goal**: Comprehensive testing and documentation

**Tasks:**
1. Create comprehensive test suite
2. Performance optimization
3. Code documentation
4. API documentation
5. Example implementations

**Deliverables:**
- Comprehensive tests
- Optimized performance
- Complete documentation
- Example console app

## Project Structure

```
Majik/
├── Majik.Core/                    # Core engine library
│   ├── StateMachine/
│   │   ├── IState.cs
│   │   ├── StateMachine.cs
│   │   ├── GameStateMachine.cs
│   │   ├── TurnStateMachine.cs
│   │   └── PhaseStateMachine.cs
│   ├── Events/
│   │   ├── GameEvent.cs
│   │   ├── IEventBus.cs
│   │   ├── EventBus.cs
│   │   └── EventTypes.cs
│   ├── Game/
│   │   ├── GameState.cs
│   │   ├── TurnManager.cs
│   │   ├── PhaseManager.cs
│   │   └── PriorityManager.cs
│   ├── Zones/
│   │   ├── IZone.cs
│   │   ├── Zone.cs
│   │   ├── ZoneManager.cs
│   │   └── ZoneTypes.cs
│   ├── Cards/
│   │   ├── ICard.cs
│   │   ├── Card.cs
│   │   ├── Permanent.cs
│   │   ├── Spell.cs
│   │   └── CardTypes/
│   ├── Abilities/
│   │   ├── IAbility.cs
│   │   ├── TriggeredAbility.cs
│   │   ├── ActivatedAbility.cs
│   │   ├── StaticAbility.cs
│   │   └── AbilityResolver.cs
│   ├── Players/
│   │   ├── Player.cs
│   │   └── PlayerState.cs
│   ├── Combat/
│   │   ├── CombatManager.cs
│   │   ├── Combat.cs
│   │   └── DamageAssignment.cs
│   ├── Rules/
│   │   ├── RulesEngine.cs
│   │   └── StateBasedActions.cs
│   └── Majik.Core.csproj
├── Majik.Console/                 # Console testing application
│   ├── Program.cs
│   ├── GameRunner.cs
│   └── Majik.Console.csproj
├── Majik.Core.Tests/              # Unit tests (future)
│   └── Majik.Core.Tests.csproj
└── Majik.sln                      # Solution file
```

## Key Design Decisions

### 1. State Machine Pattern
**Decision**: Use hierarchical state machine
**Rationale**: 
- Cleanly models game → turn → phase hierarchy
- Easy to extend with new states
- Clear state transitions
- Supports nested state machines

### 2. Event-Driven Architecture
**Decision**: All actions emit events
**Rationale**:
- Complete UI separation
- Easy to add logging/debugging
- Enables replay functionality
- Testable through event inspection

### 3. Zone System
**Decision**: Generic zone system with type safety
**Rationale**:
- Flexible for different card types
- Type-safe operations
- Easy to extend with new zones
- Clear ownership and visibility

### 4. Ability System
**Decision**: Composable ability system
**Rationale**:
- Cards can have multiple abilities
- Abilities can be added/removed dynamically
- Easy to implement complex cards
- Clear separation of concerns

### 5. Stack and Priority
**Decision**: Explicit stack and priority management
**Rationale**:
- Core to Magic rules
- Enables complex interactions
- Clear resolution order
- Supports all card types

## Testing Strategy

### Unit Tests
- Test each component in isolation
- Mock dependencies
- Test edge cases
- Test state transitions

### Integration Tests
- Test component interactions
- Test full game flows
- Test complex scenarios
- Test event propagation

### Scenario Tests
- Test specific card interactions
- Test complex game states
- Test edge cases from rules
- Test performance with many cards

## Performance Considerations

1. **Event System**: Use efficient event dispatching
2. **State Machine**: Minimize state transition overhead
3. **Zone Lookups**: Use efficient data structures
4. **Ability Resolution**: Cache ability lookups where possible
5. **Stack Resolution**: Optimize stack operations

## Future Enhancements

1. **Undo/Redo**: State history for undo operations
2. **Network Play**: Multiplayer over network
3. **AI Players**: Computer-controlled players
4. **Card Database**: Integration with card databases
5. **Replay System**: Save and replay games
6. **Rule Validation**: More comprehensive rule checking
7. **Performance Profiling**: Built-in performance tools

## Dependencies

- **.NET 8.0+**: Target framework
- **No external dependencies**: Keep engine pure (initially)
- **Future**: May add JSON serialization for card data

## Getting Started

After implementation, the basic usage will be:

```csharp
using Majik.Core;
using Majik.Core.Events;

// Create event bus
var eventBus = new EventBus();

// Subscribe to events
eventBus.Subscribe<CardDrawnEvent>(evt => 
    Console.WriteLine($"Card drawn: {evt.Card.Name}"));

// Create game
var game = new GameState(eventBus);
game.AddPlayer("Player 1");
game.AddPlayer("Player 2");

// Start game
game.StartGame();

// Game runs through state machine
// Events fire for all actions
```

## Conclusion

This plan provides a comprehensive roadmap for implementing a Magic: The Gathering game engine in C#. The architecture is designed to be:
- **Extensible**: Easy to add new features
- **Testable**: Clear separation of concerns
- **Maintainable**: Well-organized structure
- **Performant**: Efficient implementations
- **UI-Agnostic**: Complete event-driven design

The phased approach allows for incremental development and testing, ensuring each component works correctly before moving to the next phase.
