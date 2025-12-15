# Phase 1.5: Object-Oriented Design & Domain-Driven Design Refactoring

## Overview

Phase 1.5 focuses on improving code quality through proper object-oriented design principles and Domain-Driven Design (DDD) patterns. This refactoring will make the codebase more maintainable, testable, and aligned with domain concepts.

## Why DDD Makes Sense Here

Domain-Driven Design is **highly applicable** to a Magic: The Gathering engine because:

1. **Rich Domain Model**: Magic has complex rules, entities, and business logic
2. **Clear Bounded Contexts**: Game Rules, Cards, Players, Combat, Abilities
3. **Domain Events**: We already have events - they should be true domain events
4. **Aggregates**: Game is a natural aggregate root with clear boundaries
5. **Value Objects**: Mana costs, life totals, card identities are natural value objects
6. **Domain Services**: Complex operations like zone management, combat resolution

## Current Issues to Address

### 1. Encapsulation Problems
- `Player.LifeTotal` has public setter - should be encapsulated
- `Card.Zone` has public setter - should be managed through domain service
- Many properties are too exposed

### 2. Primitive Obsession
- `ManaCost` is a string - should be a value object
- `LifeTotal` is an int - could be a value object
- No validation on domain values

### 3. Anemic Domain Model
- `GameState` does too much coordination - should be split
- Domain logic scattered across multiple classes
- Missing domain services

### 4. Missing DDD Patterns
- No clear aggregate roots
- No value objects
- No domain services
- Events are infrastructure, not domain events

## Phase 1.5 Goals

1. **Implement DDD Patterns**: Aggregates, Entities, Value Objects, Domain Services
2. **Improve Encapsulation**: Make domain objects protect their invariants
3. **Create Value Objects**: Replace primitives with meaningful value objects
4. **Establish Aggregate Roots**: Clear boundaries and consistency
5. **Domain Events**: Convert infrastructure events to domain events
6. **Domain Services**: Extract complex operations to services

## Implementation Plan

### Task 1: Create Value Objects

#### 1.1 ManaCost Value Object
**Location**: `Majik.Core/ValueObjects/ManaCost.cs`

**Purpose**: Replace string-based mana costs with a proper value object

**Features**:
- Parse mana cost strings (e.g., "3RR" → 3 generic + 2 red)
- Immutable
- Equality by value
- Validation
- Conversion to string

**Properties**:
- Generic mana count
- Color mana counts (W, U, B, R, G)
- Special costs (X, Phyrexian, etc.)

#### 1.2 LifeTotal Value Object
**Location**: `Majik.Core/ValueObjects/LifeTotal.cs`

**Purpose**: Encapsulate life total with validation

**Features**:
- Immutable
- Validation (can't go below 0 without losing)
- Operations (add, subtract)
- Equality by value

#### 1.3 CardIdentity Value Object
**Location**: `Majik.Core/ValueObjects/CardIdentity.cs`

**Purpose**: Uniquely identify a card

**Features**:
- Card name
- Set code (optional)
- Collector number (optional)
- Equality by value

### Task 2: Refactor Domain Entities

#### 2.1 Card Entity
**Location**: `Majik.Core/Cards/Card.cs`

**Changes**:
- Make `Zone` private, managed through domain service
- Use `ManaCost` value object instead of string
- Add `CardIdentity` value object
- Encapsulate state changes
- Add domain methods (e.g., `MoveToZone`, `ChangeController`)

**Invariants**:
- Card must have an owner
- Zone transitions must be valid
- Controller must be valid

#### 2.2 Player Entity
**Location**: `Majik.Core/Players/Player.cs`

**Changes**:
- Make `LifeTotal` private, use value object
- Encapsulate life changes through methods
- Add domain methods (e.g., `LoseLife`, `GainLife`, `LoseGame`)
- Protect invariants

**Invariants**:
- Life total cannot be negative (player loses at 0 or less)
- Player must have zones

#### 2.3 Game Aggregate Root
**Location**: `Majik.Core/Game/Game.cs` (rename from GameState)

**Changes**:
- Make `Game` the aggregate root
- Encapsulate player collection
- Manage game lifecycle
- Coordinate domain services
- Protect game invariants

**Invariants**:
- Game must have at least 2 players
- Only one active player at a time
- Game state transitions must be valid

### Task 3: Create Domain Services

#### 3.1 ZoneService
**Location**: `Majik.Core/Services/ZoneService.cs`

**Purpose**: Manage zone operations as domain logic

**Responsibilities**:
- Move cards between zones
- Validate zone transitions
- Emit domain events
- Enforce zone rules

#### 3.2 GameService
**Location**: `Majik.Core/Services/GameService.cs`

**Purpose**: Orchestrate game operations

**Responsibilities**:
- Start game
- Manage turns
- Coordinate state machines
- Validate game rules

#### 3.3 PlayerService
**Location**: `Majik.Core/Services/PlayerService.cs`

**Purpose**: Manage player operations

**Responsibilities**:
- Create players
- Manage life totals
- Handle player loss
- Validate player actions

### Task 4: Domain Events

#### 4.1 Convert to Domain Events
**Location**: `Majik.Core/DomainEvents/`

**Changes**:
- Create `IDomainEvent` interface
- Convert existing events to domain events
- Ensure events represent domain occurrences
- Keep infrastructure events separate

**Domain Events**:
- `GameStarted`
- `CardDrawn`
- `CardMoved`
- `LifeChanged`
- `PlayerLost`
- `PhaseChanged`

### Task 5: Improve Encapsulation

#### 5.1 Make Collections Read-Only
- Use `IReadOnlyList` or `IReadOnlyCollection`
- Provide domain methods for modifications
- Protect internal state

#### 5.2 Add Validation
- Validate inputs in constructors
- Validate state transitions
- Throw domain exceptions for invalid operations

#### 5.3 Domain Exceptions
**Location**: `Majik.Core/Exceptions/`

**Create**:
- `DomainException` base class
- `InvalidGameStateException`
- `InvalidZoneTransitionException`
- `InvalidPlayerActionException`

### Task 6: Repository Pattern (Optional)

#### 6.1 Card Repository Interface
**Location**: `Majik.Core/Repositories/ICardRepository.cs`

**Purpose**: Abstract card loading (for future card database integration)

**Note**: Implementation can be deferred, but interface helps with design

## New Project Structure

```
Majik.Core/
├── Domain/                      # Domain layer
│   ├── Entities/                # Domain entities
│   │   ├── Card.cs
│   │   ├── Player.cs
│   │   └── Game.cs
│   ├── ValueObjects/            # Value objects
│   │   ├── ManaCost.cs
│   │   ├── LifeTotal.cs
│   │   └── CardIdentity.cs
│   ├── Aggregates/              # Aggregate roots
│   │   └── Game.cs
│   ├── DomainEvents/            # Domain events
│   │   ├── IDomainEvent.cs
│   │   ├── GameStarted.cs
│   │   └── ...
│   └── Exceptions/              # Domain exceptions
│       ├── DomainException.cs
│       └── ...
├── Services/                    # Domain services
│   ├── ZoneService.cs
│   ├── GameService.cs
│   └── PlayerService.cs
├── Infrastructure/              # Infrastructure layer
│   ├── Events/                  # Infrastructure events (event bus)
│   │   ├── IEventBus.cs
│   │   └── EventBus.cs
│   └── StateMachine/            # State machine (infrastructure)
├── Cards/                       # Card types (keep for now)
├── Zones/                       # Zone types (keep for now)
└── StateMachine/                # State machine (keep for now)
```

## Implementation Order

1. **Value Objects** (Foundation)
   - ManaCost
   - LifeTotal
   - CardIdentity

2. **Domain Exceptions** (Foundation)
   - DomainException base
   - Specific exceptions

3. **Refactor Entities** (Core)
   - Card entity
   - Player entity
   - Game aggregate

4. **Domain Services** (Coordination)
   - ZoneService
   - PlayerService
   - GameService

5. **Domain Events** (Communication)
   - Convert to domain events
   - Keep infrastructure events separate

6. **Testing** (Validation)
   - Update console app
   - Verify all functionality works

## Key Design Principles

### 1. Encapsulation
- Private setters for state
- Public methods for operations
- Protect invariants

### 2. Immutability (Where Appropriate)
- Value objects are immutable
- Domain events are immutable
- Entities have mutable state but protected

### 3. Single Responsibility
- Each class has one reason to change
- Services handle coordination
- Entities handle their own state

### 4. Dependency Inversion
- Depend on abstractions (interfaces)
- Domain doesn't depend on infrastructure
- Services depend on domain interfaces

### 5. Domain Language
- Use Magic: The Gathering terminology
- Methods reflect domain operations
- Classes reflect domain concepts

## Benefits of This Refactoring

1. **Better Encapsulation**: Domain objects protect their invariants
2. **Type Safety**: Value objects prevent invalid states
3. **Testability**: Clear boundaries make testing easier
4. **Maintainability**: DDD patterns make code easier to understand
5. **Extensibility**: Clear structure makes adding features easier
6. **Domain Alignment**: Code reflects the Magic: The Gathering domain

## Migration Strategy

1. **Incremental**: Refactor one component at a time
2. **Backward Compatible**: Keep existing interfaces where possible
3. **Test Continuously**: Ensure functionality works after each change
4. **Document Changes**: Update documentation as we go

## Success Criteria

- ✅ All value objects are immutable and validated
- ✅ All entities protect their invariants
- ✅ Domain services handle complex operations
- ✅ Domain events represent domain occurrences
- ✅ Code compiles and tests pass
- ✅ Console app demonstrates all functionality
- ✅ Code follows DDD patterns consistently

## Estimated Effort

- Value Objects: 2-3 hours
- Domain Exceptions: 1 hour
- Entity Refactoring: 3-4 hours
- Domain Services: 2-3 hours
- Domain Events: 2 hours
- Testing & Validation: 2 hours

**Total**: ~12-15 hours

## Next Steps After Phase 1.5

After completing Phase 1.5, we'll have:
- A well-structured domain model
- Clear separation of concerns
- Better encapsulation
- Foundation for Phase 2 (Turn and Phase Management)

The refactored code will be much easier to extend with new features while maintaining code quality.
