# Phase 3: Stack and Priority Management - Implementation Complete

## Overview

Phase 3 has been successfully implemented, providing the stack and priority system that enables players to cast spells, activate abilities, and respond to each other's actions. The implementation follows the official Magic: The Gathering Comprehensive Rules (2025-11-14).

## Completed Components

### ✅ 1. Stack System

**Location**: `Majik.Core/Stack/`

**Created**:
- `IStackObject`: Interface for objects on the stack
- `Stack`: LIFO stack implementation
- `StackResolver`: Handles stack resolution

**Features**:
- LIFO (Last In, First Out) structure (Rule 405.2)
- Type-safe stack objects
- Stack change events
- Resolution tracking

**Key Methods**:
- `Push(IStackObject)`: Add object to stack
- `ResolveTop()`: Resolve top object
- `IsEmpty`: Check if stack is empty
- `Count`: Number of objects on stack

### ✅ 2. Priority System

**Location**: `Majik.Core/Game/PriorityManager.cs`

**Features**:
- Priority passing in turn order (APNAP - Rule 117.3d)
- Active player gets priority first (Rule 117.3a)
- All players must pass for phase to end (Rule 117.4)
- Stack must be empty for phase to end (Rule 500.2)
- Priority hold support (Rule 117.3c)

**Key Methods**:
- `InitializeForPhase(Player)`: Initialize priority for phase
- `GivePriority(Player)`: Give priority to player
- `PassPriority()`: Pass priority to next player
- `CanEndPhase()`: Check if phase can end
- `HoldPriority()`: Hold priority after casting/activating

### ✅ 3. Stack Resolution

**Location**: `Majik.Core/Stack/StackResolver.cs`

**Features**:
- Resolve top of stack
- Handle resolution effects
- Trigger resolution events
- Support for resolving all objects

**Resolution Process** (Rule 405.5, 608):
1. Remove object from stack
2. Execute object's effects
3. Fire resolution event
4. Move to appropriate zone (future)

### ✅ 4. Phase Integration

**Location**: `Majik.Core/Game/PhaseManager.cs` (updated)

**Integration Points**:
- Phases check stack before ending
- Priority given at beginning of phases (Rule 117.3a)
- Phases wait for stack to empty (Rule 500.2)
- All players must pass before phase ends (Rule 117.4)

**Updated Methods**:
- `CanAutoAdvance()`: Now checks stack and priority
- `ProcessAllPhases()`: Now handles priority during phases
- `NeedsPriority()`: Determines which phases need priority

### ✅ 5. Spell Foundation

**Location**: `Majik.Core/Spells/`

**Created**:
- `ISpell`: Interface for spells on the stack
- `Spell`: Base spell implementation
- `SpellCaster`: Service for casting spells

**Features**:
- Spells can be added to stack
- Basic casting validation
- Foundation for full spell casting (Phase 4)

### ✅ 6. Ability Foundation

**Location**: `Majik.Core/Abilities/`

**Created**:
- `IActivatedAbility`: Interface for activated abilities
- `ActivatedAbility`: Base activated ability implementation
- `AbilityActivator`: Service for activating abilities

**Features**:
- Abilities can be added to stack
- Basic activation validation
- Foundation for full ability system (Phase 4)

### ✅ 7. Stack and Priority Events

**Location**: `Majik.Core/Events/`

**Created Events**:
- `StackObjectAddedEvent`: Fired when object added to stack
- `StackObjectResolvedEvent`: Fired when object resolves
- `StackClearedEvent`: Fired when stack is cleared
- `PriorityReceivedEvent`: Fired when player receives priority
- `PriorityPassedEvent`: Fired when player passes priority
- `AllPlayersPassedEvent`: Fired when all players pass

## Test Results

The console application successfully demonstrates:

### Stack Operations
- ✅ Objects can be added to stack
- ✅ Stack resolves in LIFO order (Counterspell resolves before Lightning Bolt)
- ✅ Stack events fire correctly
- ✅ Stack can be cleared

### Priority Passing
- ✅ Active player receives priority first
- ✅ Priority passes in turn order (APNAP)
- ✅ All players passing triggers resolution or phase end
- ✅ Priority events fire correctly

### Phase Integration
- ✅ Phases check stack before ending
- ✅ Priority given at beginning of phases
- ✅ Untap phase correctly doesn't give priority (Rule 502.4)
- ✅ Phases wait for all players to pass

### Sample Output
```
Alice casts Lightning Bolt:
    [Stack] Alice casts Lightning Bolt
Bob responds with Counterspell:
    [Stack] Bob casts Counterspell

Stack has 2 objects
Top of stack: Spell

Resolving stack (LIFO order):
    [Stack] Counterspell resolves  ← Resolves first (LIFO)
    [Stack] Lightning Bolt resolves
```

## Rules Compliance

### Priority Rules (Rule 117)
- ✅ Active player gets priority first (Rule 117.3a)
- ✅ Priority passes in turn order (Rule 117.3d)
- ✅ All players must pass for phase to end (Rule 117.4)
- ✅ Stack must be empty for phase to end (Rule 500.2)
- ✅ Player can hold priority (Rule 117.3c)

### Stack Rules (Rule 405)
- ✅ LIFO resolution order (Rule 405.2)
- ✅ Objects resolve one at a time (Rule 405.5)
- ✅ Stack can be empty (Rule 405.4)
- ✅ Players can respond (Rule 117.7)

### Phase Rules (Rule 500)
- ✅ Phases wait for stack to empty (Rule 500.2)
- ✅ Priority given at appropriate times (Rule 117.3a)
- ✅ Untap step doesn't give priority (Rule 502.4)

## Architecture Improvements

### Before Phase 3
- ❌ No stack system
- ❌ No priority system
- ❌ Phases auto-advanced without checking stack
- ❌ No way to cast spells or activate abilities

### After Phase 3
- ✅ Complete stack system (LIFO)
- ✅ Full priority passing system
- ✅ Phases integrate with stack/priority
- ✅ Foundation for spell casting
- ✅ Foundation for ability activation
- ✅ Players can respond to each other

## Files Created

### Stack System (3 files)
- `Stack/IStackObject.cs`
- `Stack/Stack.cs`
- `Stack/StackResolver.cs`

### Priority System (1 file)
- `Game/PriorityManager.cs`

### Spell Foundation (3 files)
- `Spells/ISpell.cs`
- `Spells/Spell.cs`
- `Spells/SpellCaster.cs`

### Ability Foundation (3 files)
- `Abilities/IActivatedAbility.cs`
- `Abilities/ActivatedAbility.cs`
- `Abilities/AbilityActivator.cs`

### Events (6 files)
- `Events/StackObjectAddedEvent.cs`
- `Events/StackObjectResolvedEvent.cs`
- `Events/StackClearedEvent.cs`
- `Events/PriorityReceivedEvent.cs`
- `Events/PriorityPassedEvent.cs`
- `Events/AllPlayersPassedEvent.cs`

**Total**: 16 new files created

## Files Modified

### Core Game Logic
- `Game/PhaseManager.cs`: Integrated with stack and priority
- `Domain/Aggregates/Game.cs`: Added stack and priority manager

## Build Status

✅ **All code compiles successfully with 0 errors and 0 warnings**

## Success Criteria Met

### Functional Requirements
- ✅ Stack stores spells and abilities
- ✅ Stack resolves in LIFO order
- ✅ Priority passes correctly
- ✅ Phases wait for stack to empty
- ✅ Players can cast spells (foundation)
- ✅ Players can activate abilities (foundation)
- ✅ Players can respond to each other

### Technical Requirements
- ✅ Code follows DDD patterns
- ✅ All code compiles with 0 errors/warnings
- ✅ Stack events fire correctly
- ✅ Priority events fire correctly
- ✅ Integration with phase system works

### Testing Requirements
- ✅ Console app demonstrates stack resolution
- ✅ Console app demonstrates priority passing
- ✅ Phases correctly wait for stack
- ✅ All events fire correctly

## Key Design Decisions

### 1. Stack Implementation
**Decision**: Use `Stack<IStackObject>` for LIFO structure
**Rationale**: 
- Matches Magic rules exactly (Rule 405.2)
- Efficient push/pop operations
- Clear semantics

### 2. Priority Passing
**Decision**: Explicit priority manager with turn order
**Rationale**:
- Matches Magic rules (APNAP order - Rule 117.3d)
- Clear priority flow
- Easy to track current player

### 3. Phase Integration
**Decision**: Phases check stack before auto-advancing
**Rationale**:
- Matches Magic rules (Rule 500.2)
- Phases can't end with stack not empty
- All players must pass

### 4. Foundation Classes
**Decision**: Create basic spell/ability classes now
**Rationale**:
- Enables testing stack/priority
- Foundation for Phase 4
- Clear separation of concerns

## Next Steps (Phase 4)

After completing Phase 3, we're ready for Phase 4: Card System and Abilities. This will add:
- Full spell casting with costs and targeting
- Complete ability system
- Triggered abilities
- Static abilities
- Card abilities implementation

The stack and priority system from Phase 3 provides the foundation for these features.

## Summary

Phase 3 successfully implements the stack and priority system, enabling:
- Players to cast spells and activate abilities
- Players to respond to each other
- Proper stack resolution in LIFO order
- Priority passing according to Magic rules
- Phase integration with stack/priority

The implementation follows the official Comprehensive Rules and provides a solid foundation for Phase 4.
