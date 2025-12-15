# Phase 3.5: DDD & OOP Refactoring for Stack and Priority System

## Overview

Phase 3.5 focuses on refactoring the Phase 3 stack and priority implementation to better align with Domain-Driven Design (DDD) and Object-Oriented Programming (OOP) principles. This refactoring will improve encapsulation, introduce value objects where appropriate, reorganize services, and strengthen domain boundaries without removing functionality.

## Goals

1. **Improve Encapsulation**: Protect internal state and invariants
2. **Introduce Value Objects**: Replace primitives with meaningful domain concepts
3. **Reorganize Services**: Move services to proper domain service locations
4. **Strengthen Domain Boundaries**: Better separation between domain and infrastructure
5. **Improve Immutability**: Make domain objects more immutable where appropriate
6. **Enhance Domain Events**: Organize events as true domain events

## Analysis of Current Phase 3 Code

### Issues Identified

#### 1. Stack Class (`Majik.Core/Stack/Stack.cs`)

**Issues**:
- ❌ Has resolution logic (`ResolveTop()`) - violates Single Responsibility Principle
- ❌ Directly publishes events - should delegate to domain service
- ❌ Exposes internal collection via `GetAll()` - breaks encapsulation
- ❌ Mixing infrastructure (events) with domain logic

**Improvements**:
- ✅ Move resolution logic to `StackResolver`
- ✅ Make `GetAll()` return read-only collection
- ✅ Consider if Stack should be an entity or part of Game aggregate
- ✅ Better encapsulation of internal state

#### 2. PriorityManager (`Majik.Core/Game/PriorityManager.cs`)

**Issues**:
- ❌ Primitive obsession: `_passCount` as int - should be value object
- ❌ Too many responsibilities (priority passing + phase end checking)
- ❌ Exposes internal state (`CurrentPlayer`, `AllPlayersPassed`)
- ❌ Direct dependency on Stack (should be through abstraction)

**Improvements**:
- ✅ Create `PriorityState` value object
- ✅ Better encapsulation of priority state
- ✅ Extract phase end checking to domain service
- ✅ Use abstraction for stack dependency

#### 3. Spell and ActivatedAbility Entities

**Issues**:
- ❌ Mutable `_isResolving` flag - should be immutable or better encapsulated
- ❌ Resolution state could be a value object
- ❌ No clear lifecycle management

**Improvements**:
- ✅ Create `ResolutionState` value object
- ✅ Better encapsulation of resolution state
- ✅ Consider making resolution immutable

#### 4. Service Organization

**Issues**:
- ❌ `SpellCaster` and `AbilityActivator` are not domain services (wrong namespace)
- ❌ `StackResolver` is not a domain service
- ❌ Services should be in `Domain/Services/` or `Services/` consistently

**Improvements**:
- ✅ Move services to proper locations
- ✅ Create domain service interfaces if needed
- ✅ Ensure consistent service organization

#### 5. Domain Events

**Issues**:
- ❌ Stack and priority events are in `Events/` but should be in `Domain/DomainEvents/`
- ❌ Mixing infrastructure events with domain events

**Improvements**:
- ✅ Move domain events to `Domain/DomainEvents/`
- ✅ Keep infrastructure events separate
- ✅ Ensure events are true domain events

#### 6. Encapsulation Issues

**Issues**:
- ❌ Stack exposes internal collection
- ❌ PriorityManager exposes too much state
- ❌ Services have direct dependencies on concrete classes

**Improvements**:
- ✅ Return read-only collections
- ✅ Hide internal state better
- ✅ Use abstractions where appropriate

## Implementation Plan

### Task 1: Create Value Objects

#### 1.1 PriorityState Value Object
**Location**: `Majik.Core/Domain/ValueObjects/PriorityState.cs`

**Purpose**: Encapsulate priority state (current player, pass count, active player)

**Properties**:
- `CurrentPlayer`: Player with priority
- `ActivePlayer`: Active player for the phase
- `PassCount`: Number of consecutive passes
- `AllPlayersPassed`: Computed property

**Features**:
- Immutable
- Equality by value
- Validation
- Factory methods

#### 1.2 ResolutionState Value Object
**Location**: `Majik.Core/Domain/ValueObjects/ResolutionState.cs`

**Purpose**: Encapsulate resolution state for stack objects

**Properties**:
- `IsResolving`: Whether object is resolving
- `ResolvedAt`: Timestamp of resolution (optional)

**Features**:
- Immutable
- Factory methods for states
- Validation

### Task 2: Refactor Stack Class

**Location**: `Majik.Core/Stack/Stack.cs`

**Changes**:
1. Remove `ResolveTop()` method (move to StackResolver)
2. Make `GetAll()` return `IReadOnlyList<IStackObject>`
3. Better encapsulation of internal state
4. Remove direct event publishing (delegate to service)

**Before**:
```csharp
public IStackObject? ResolveTop() { ... }
public IEnumerable<IStackObject> GetAll() { ... }
```

**After**:
```csharp
// Resolution moved to StackResolver
public IReadOnlyList<IStackObject> GetAll() { ... }
```

### Task 3: Refactor PriorityManager

**Location**: `Majik.Core/Game/PriorityManager.cs`

**Changes**:
1. Use `PriorityState` value object instead of primitives
2. Better encapsulation of state
3. Extract phase end checking to domain service
4. Use abstraction for stack dependency

**Before**:
```csharp
private Player? _currentPlayer;
private Player? _activePlayer;
private int _passCount;
```

**After**:
```csharp
private PriorityState _state;
```

### Task 4: Refactor Spell and ActivatedAbility

**Location**: `Majik.Core/Spells/Spell.cs`, `Majik.Core/Abilities/ActivatedAbility.cs`

**Changes**:
1. Use `ResolutionState` value object
2. Better encapsulation of resolution state
3. Make resolution immutable where possible

**Before**:
```csharp
private bool _isResolving;
public bool IsResolving => _isResolving;
```

**After**:
```csharp
private ResolutionState _resolutionState;
public bool IsResolving => _resolutionState.IsResolving;
```

### Task 5: Reorganize Services

**Changes**:
1. Move `SpellCaster` to `Majik.Core/Services/SpellCaster.cs`
2. Move `AbilityActivator` to `Majik.Core/Services/AbilityActivator.cs`
3. Move `StackResolver` to `Majik.Core/Services/StackResolver.cs`
4. Consider creating domain service interfaces

**File Moves**:
- `Spells/SpellCaster.cs` → `Services/SpellCaster.cs`
- `Abilities/AbilityActivator.cs` → `Services/AbilityActivator.cs`
- `Stack/StackResolver.cs` → `Services/StackResolver.cs`

### Task 6: Organize Domain Events

**Changes**:
1. Move stack/priority domain events to `Domain/DomainEvents/`
2. Keep infrastructure events in `Events/`
3. Ensure events are true domain events

**File Moves**:
- `Events/StackObjectAddedEvent.cs` → `Domain/DomainEvents/StackObjectAddedEvent.cs`
- `Events/StackObjectResolvedEvent.cs` → `Domain/DomainEvents/StackObjectResolvedEvent.cs`
- `Events/StackClearedEvent.cs` → `Domain/DomainEvents/StackClearedEvent.cs`
- `Events/PriorityReceivedEvent.cs` → `Domain/DomainEvents/PriorityReceivedEvent.cs`
- `Events/PriorityPassedEvent.cs` → `Domain/DomainEvents/PriorityPassedEvent.cs`
- `Events/AllPlayersPassedEvent.cs` → `Domain/DomainEvents/AllPlayersPassedEvent.cs`

### Task 7: Improve StackResolver

**Location**: `Majik.Core/Services/StackResolver.cs` (after move)

**Changes**:
1. Move resolution logic from Stack to StackResolver
2. Better separation of concerns
3. Handle event publishing

**Before** (in Stack):
```csharp
public IStackObject? ResolveTop()
{
    var top = _objects.Pop();
    top.Resolve();
    _eventBus?.Publish(new StackObjectResolvedEvent(top));
    return top;
}
```

**After** (in StackResolver):
```csharp
public IStackObject? ResolveTop(Stack stack)
{
    if (stack.IsEmpty) return null;
    
    var top = stack.Pop(); // Stack only pops, doesn't resolve
    top.Resolve();
    _eventBus?.Publish(new StackObjectResolvedEvent(top));
    return top;
}
```

### Task 8: Update Stack Interface

**Location**: `Majik.Core/Stack/Stack.cs`

**Changes**:
1. Remove `ResolveTop()` method
2. Keep `Pop()` for StackResolver to use
3. Make `Pop()` internal or protected if needed

### Task 9: Improve Encapsulation Throughout

**Changes**:
1. Make `GetAll()` return `IReadOnlyList<IStackObject>`
2. Hide internal state in PriorityManager
3. Use value objects for state

## Design Decisions

### 1. Stack as Entity vs Part of Aggregate

**Decision**: Stack is part of Game aggregate
**Rationale**: 
- Stack is tightly coupled to game state
- Only one stack per game
- Stack lifecycle matches game lifecycle

### 2. PriorityState as Value Object

**Decision**: Create PriorityState value object
**Rationale**:
- Encapsulates related state
- Immutable ensures consistency
- Better than primitive obsession

### 3. ResolutionState as Value Object

**Decision**: Create ResolutionState value object
**Rationale**:
- Encapsulates resolution state
- Immutable ensures consistency
- Better than boolean flag

### 4. Service Organization

**Decision**: Move services to `Services/` directory
**Rationale**:
- Consistent with existing services (PlayerService, ZoneService)
- Clear separation of concerns
- Easy to find and maintain

### 5. Domain Events Organization

**Decision**: Move domain events to `Domain/DomainEvents/`
**Rationale**:
- True domain events belong in domain layer
- Infrastructure events stay in Events/
- Clear separation

## Success Criteria

### Functional Requirements
- ✅ All existing functionality preserved
- ✅ Stack still works correctly
- ✅ Priority passing still works correctly
- ✅ Stack resolution still works correctly
- ✅ All tests pass

### Technical Requirements
- ✅ Better encapsulation
- ✅ Value objects introduced
- ✅ Services properly organized
- ✅ Domain events organized
- ✅ Code compiles with 0 errors/warnings
- ✅ No breaking changes to public API (where possible)

### DDD Requirements
- ✅ Clear value objects
- ✅ Better encapsulation
- ✅ Proper service organization
- ✅ Domain events in domain layer
- ✅ Clear aggregate boundaries

## Files to Create

### Value Objects (2 files)
- `Domain/ValueObjects/PriorityState.cs`
- `Domain/ValueObjects/ResolutionState.cs`

**Total**: 2 new files

## Files to Modify

### Stack System (2 files)
- `Stack/Stack.cs`: Remove ResolveTop, improve encapsulation
- `Stack/IStackObject.cs`: No changes needed

### Priority System (1 file)
- `Game/PriorityManager.cs`: Use PriorityState value object

### Spell/Ability System (2 files)
- `Spells/Spell.cs`: Use ResolutionState value object
- `Abilities/ActivatedAbility.cs`: Use ResolutionState value object

### Services (3 files)
- `Services/SpellCaster.cs`: Move from Spells/
- `Services/AbilityActivator.cs`: Move from Abilities/
- `Services/StackResolver.cs`: Move from Stack/, add resolution logic

### Domain Events (6 files)
- `Domain/DomainEvents/StackObjectAddedEvent.cs`: Move from Events/
- `Domain/DomainEvents/StackObjectResolvedEvent.cs`: Move from Events/
- `Domain/DomainEvents/StackClearedEvent.cs`: Move from Events/
- `Domain/DomainEvents/PriorityReceivedEvent.cs`: Move from Events/
- `Domain/DomainEvents/PriorityPassedEvent.cs`: Move from Events/
- `Domain/DomainEvents/AllPlayersPassedEvent.cs`: Move from Events/

### Game Aggregate (1 file)
- `Domain/Aggregates/Game.cs`: Update references to moved services/events

**Total**: 15 files to modify

## Implementation Order

1. **Create Value Objects** (PriorityState, ResolutionState)
2. **Refactor Stack** (remove ResolveTop, improve encapsulation)
3. **Refactor PriorityManager** (use PriorityState)
4. **Refactor Spell/Ability** (use ResolutionState)
5. **Move Services** (SpellCaster, AbilityActivator, StackResolver)
6. **Move Domain Events** (stack/priority events)
7. **Update StackResolver** (add resolution logic from Stack)
8. **Update References** (Game.cs, PhaseManager.cs, etc.)
9. **Test and Verify** (ensure everything works)

## Testing Strategy

1. **Unit Tests**: Test value objects
2. **Integration Tests**: Test stack/priority system
3. **Console App**: Verify functionality preserved
4. **Build Verification**: Ensure 0 errors/warnings

## Notes

- This refactoring does NOT remove functionality
- All public APIs preserved where possible
- Internal improvements only
- Better DDD/OOP alignment
- Improved maintainability
