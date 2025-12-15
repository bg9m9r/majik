# Phase 3: Stack and Priority Management - Implementation Plan

## Overview

Phase 3 focuses on implementing the stack and priority system, which are fundamental to Magic: The Gathering gameplay. This phase will enable players to cast spells, activate abilities, and respond to each other's actions. The implementation will follow the official Comprehensive Rules (2025-11-14) for timing and priority.

## Goals

1. **Stack System**: Implement LIFO stack for spells and abilities
2. **Priority System**: Implement priority passing between players
3. **Stack Resolution**: Resolve spells and abilities in correct order
4. **Phase Integration**: Integrate stack/priority with phase system
5. **Player Actions**: Enable casting spells and activating abilities
6. **Response System**: Allow players to respond to each other

## Current State Analysis

### What We Have (Phase 1, 1.5, 2, 2.75)
- ✅ Complete turn and phase sequencing
- ✅ Automatic phase progression
- ✅ Event system
- ✅ Game aggregate root
- ✅ Domain services
- ✅ Zone system
- ✅ Basic card classes

### What's Missing
- ❌ Stack class for spells/abilities
- ❌ PriorityManager for priority passing
- ❌ Stack resolution logic
- ❌ Integration with phases (phases should wait for stack to empty)
- ❌ Ability to cast spells
- ❌ Ability to activate abilities
- ❌ Response system

## Rules Reference

### Priority Rules (Rule 117)

**Rule 117.1**: Player with priority may cast spells, activate abilities, take special actions

**Rule 117.3a**: Active player receives priority at beginning of most steps/phases, after turn-based actions and triggered abilities are put on stack

**Rule 117.3b**: Active player receives priority after a spell or ability resolves

**Rule 117.3c**: Player receives priority after casting a spell, activating an ability, or taking a special action

**Rule 117.3d**: If player passes, next player in turn order receives priority

**Rule 117.4**: If all players pass in succession:
- If stack not empty: Top object resolves
- If stack empty: Phase or step ends

**Rule 117.5**: Before player receives priority:
1. Perform all state-based actions
2. Put triggered abilities on stack
3. Repeat until no more state-based actions or triggers
4. Then player receives priority

**Rule 117.7**: Players can cast spells/abilities "in response" - new object resolves first (LIFO)

### Stack Rules (Rule 405)

**Rule 405.1**: Stack is the zone where spells and abilities exist before they resolve

**Rule 405.2**: Objects on stack resolve one at a time, last-in-first-out (LIFO)

**Rule 405.3**: When an object resolves, it's removed from stack and its effects occur

**Rule 405.4**: Stack can be empty

**Rule 405.5**: Players can respond to objects on stack

## Implementation Tasks

### Task 1: Stack Implementation

**Location**: `Majik.Core/Stack/Stack.cs`

**Purpose**: Manages the spell/ability stack (LIFO structure)

**Key Responsibilities**:
- Store spells and abilities
- Maintain LIFO order
- Track resolution state
- Emit stack events

**Key Methods**:
```csharp
public class Stack
{
    public bool IsEmpty { get; }
    public int Count { get; }
    public IStackObject? Top { get; }
    public void Push(IStackObject stackObject);
    public IStackObject? Pop();
    public IStackObject? ResolveTop();
    public IEnumerable<IStackObject> GetAll();
    public void Clear();
}
```

**Features**:
- LIFO (Last In, First Out) structure
- Type-safe stack objects
- Stack change events
- Resolution tracking

**Domain Events**:
- `StackObjectAddedEvent`: Fired when object added to stack
- `StackObjectResolvedEvent`: Fired when object resolves
- `StackClearedEvent`: Fired when stack is cleared

### Task 2: Stack Object Interface

**Location**: `Majik.Core/Stack/IStackObject.cs`

**Purpose**: Base interface for objects that can be on the stack

**Key Properties**:
- Unique identifier
- Controller (who cast/activated it)
- Timestamp
- Resolution state

**Implementation**:
- `Spell`: Spells on the stack
- `ActivatedAbility`: Activated abilities on the stack
- `TriggeredAbility`: Triggered abilities on the stack (future)

### Task 3: PriorityManager Implementation

**Location**: `Majik.Core/Game/PriorityManager.cs`

**Purpose**: Manages priority passing between players

**Key Responsibilities**:
- Track current player with priority
- Handle priority passing
- Determine when phase can end
- Support priority holds

**Key Methods**:
```csharp
public class PriorityManager
{
    public Player? CurrentPlayer { get; }
    public void GivePriority(Player player);
    public void PassPriority();
    public bool CanEndPhase();
    public void HoldPriority(Player player);
}
```

**Priority Rules** (Rule 117):
- Active player gets priority first
- Priority passes in turn order (APNAP)
- All players must pass for phase to end
- Stack must be empty for phase to end
- Player can hold priority after casting/activating

**Features**:
- Turn order tracking
- Priority passing logic
- Phase end validation
- Priority hold support

**Domain Events**:
- `PriorityReceivedEvent`: Fired when player receives priority
- `PriorityPassedEvent`: Fired when player passes priority

### Task 4: Stack Resolution

**Location**: `Majik.Core/Stack/StackResolver.cs`

**Purpose**: Handles resolution of stack objects

**Key Responsibilities**:
- Resolve top of stack
- Handle resolution effects
- Trigger resolution events
- Check for state-based actions after resolution

**Key Methods**:
```csharp
public class StackResolver
{
    public void ResolveTop(Stack stack);
    public bool CanResolve(Stack stack);
    public void ResolveAll(Stack stack);
}
```

**Resolution Process** (Rule 608):
1. Remove object from stack
2. Execute object's effects
3. Move to appropriate zone (if spell)
4. Check state-based actions
5. Give priority to active player

### Task 5: Phase Integration

**Location**: `Majik.Core/Game/PhaseManager.cs` (update)

**Purpose**: Integrate stack/priority with phase system

**Key Changes**:
- Phases wait for stack to empty before ending
- Priority is given at appropriate times
- Phase can't end until all players pass with empty stack

**Updated Methods**:
- `CanAutoAdvance()`: Check if stack is empty
- `TransitionToNextPhase()`: Wait for stack to empty
- `ProcessAllPhases()`: Handle priority during phases

**Priority Points** (Rule 117.3a):
- Beginning of most steps/phases (after turn-based actions)
- After spell/ability resolves
- After player casts/activates

### Task 6: Spell Casting Foundation

**Location**: `Majik.Core/Spells/` (new)

**Purpose**: Foundation for casting spells

**Key Classes**:
- `ISpell`: Interface for spells
- `Spell`: Base spell implementation
- `SpellCaster`: Service for casting spells

**Key Methods**:
```csharp
public class SpellCaster
{
    public bool CanCast(ICard card, Player player);
    public void CastSpell(ICard card, Player player);
    public void PayCosts(ICard card, Player player);
}
```

**Note**: Full spell casting (with costs, targeting, etc.) will be in Phase 4, but foundation is laid here.

### Task 7: Ability Activation Foundation

**Location**: `Majik.Core/Abilities/` (new)

**Purpose**: Foundation for activating abilities

**Key Classes**:
- `IActivatedAbility`: Interface for activated abilities
- `ActivatedAbility`: Base activated ability implementation
- `AbilityActivator`: Service for activating abilities

**Key Methods**:
```csharp
public class AbilityActivator
{
    public bool CanActivate(IActivatedAbility ability, Player player);
    public void ActivateAbility(IActivatedAbility ability, Player player);
    public void PayCosts(IActivatedAbility ability, Player player);
}
```

**Note**: Full ability system will be in Phase 4, but foundation is laid here.

### Task 8: Stack and Priority Events

**Location**: `Majik.Core/Events/`

**Purpose**: Events for stack and priority changes

**New Events**:
- `StackObjectAddedEvent`: Object added to stack
- `StackObjectResolvedEvent`: Object resolved from stack
- `StackClearedEvent`: Stack cleared
- `PriorityReceivedEvent`: Player received priority
- `PriorityPassedEvent`: Player passed priority
- `AllPlayersPassedEvent`: All players passed (stack resolves or phase ends)

## Detailed Implementation

### Stack Structure

```csharp
public interface IStackObject
{
    Guid Id { get; }
    Player Controller { get; }
    DateTime Timestamp { get; }
    bool IsResolving { get; }
    void Resolve();
}

public class Stack
{
    private readonly Stack<IStackObject> _objects = new();
    
    public void Push(IStackObject stackObject)
    {
        _objects.Push(stackObject);
        // Fire event
    }
    
    public IStackObject? ResolveTop()
    {
        if (_objects.Count == 0) return null;
        
        var top = _objects.Pop();
        top.Resolve();
        // Fire event
        return top;
    }
}
```

### Priority Flow

```
Phase Begins
  ↓
Turn-Based Actions Execute
  ↓
Triggered Abilities Put on Stack
  ↓
Active Player Receives Priority
  ↓
[Player can cast/activate/pass]
  ↓
If Pass: Next Player Receives Priority
  ↓
If All Pass: Resolve Top of Stack OR End Phase
```

### Phase Integration

**Main Phase Example**:
1. Main phase begins
2. Turn-based actions (none for main phase)
3. Triggered abilities put on stack
4. Active player receives priority
5. Player can cast spells, activate abilities, or pass
6. If all players pass with empty stack → phase ends
7. If stack not empty → resolve top, repeat from step 4

## Implementation Order

### Step 1: Core Stack (Day 1)
1. Create `IStackObject` interface
2. Create `Stack` class
3. Implement basic push/pop/resolve
4. Add stack events

### Step 2: Priority System (Day 2)
1. Create `PriorityManager` class
2. Implement priority passing logic
3. Add priority events
4. Integrate with turn order

### Step 3: Stack Resolution (Day 3)
1. Create `StackResolver` class
2. Implement resolution logic
3. Handle resolution effects
4. Add state-based action checks

### Step 4: Phase Integration (Day 4)
1. Update `PhaseManager` to check stack
2. Integrate priority with phases
3. Update `CanAutoAdvance()` logic
4. Handle phase ending conditions

### Step 5: Foundation for Spells/Abilities (Day 5)
1. Create spell/ability interfaces
2. Create basic implementations
3. Create casting/activation services
4. Add to stack when cast/activated

### Step 6: Testing & Integration (Day 6)
1. Update console app
2. Test stack resolution
3. Test priority passing
4. Test phase integration
5. Verify all events fire

## Technical Design Decisions

### 1. Stack Implementation
**Decision**: Use `Stack<IStackObject>` for LIFO structure
**Rationale**: 
- Matches Magic rules (LIFO)
- Efficient push/pop operations
- Clear semantics

### 2. Priority Passing
**Decision**: Explicit priority manager with turn order
**Rationale**:
- Matches Magic rules (APNAP order)
- Clear priority flow
- Easy to track current player

### 3. Phase Integration
**Decision**: Phases check stack before auto-advancing
**Rationale**:
- Matches Magic rules (Rule 500.2)
- Phases can't end with stack not empty
- All players must pass

### 4. Stack Resolution Timing
**Decision**: Resolve after all players pass
**Rationale**:
- Matches Magic rules (Rule 117.4)
- Allows responses
- Correct resolution order

### 5. State-Based Actions
**Decision**: Check after each resolution
**Rationale**:
- Matches Magic rules (Rule 117.5)
- Ensures game state is valid
- Prevents invalid states

## File Structure

```
Majik.Core/
├── Stack/
│   ├── IStackObject.cs
│   ├── Stack.cs
│   ├── StackResolver.cs
│   └── StackObjectTypes.cs
├── Game/
│   ├── PriorityManager.cs
│   └── PhaseManager.cs (updated)
├── Spells/ (new)
│   ├── ISpell.cs
│   ├── Spell.cs
│   └── SpellCaster.cs
├── Abilities/ (new)
│   ├── IActivatedAbility.cs
│   ├── ActivatedAbility.cs
│   └── AbilityActivator.cs
└── Events/
    ├── StackObjectAddedEvent.cs
    ├── StackObjectResolvedEvent.cs
    ├── PriorityReceivedEvent.cs
    ├── PriorityPassedEvent.cs
    └── AllPlayersPassedEvent.cs
```

## Rules Compliance

### Priority Rules (Rule 117)
- ✅ Active player gets priority first
- ✅ Priority passes in turn order
- ✅ All players must pass for phase to end
- ✅ Stack must be empty for phase to end
- ✅ Player can hold priority

### Stack Rules (Rule 405)
- ✅ LIFO resolution order
- ✅ Objects resolve one at a time
- ✅ Stack can be empty
- ✅ Players can respond

### Phase Rules (Rule 500)
- ✅ Phases wait for stack to empty
- ✅ Priority given at appropriate times
- ✅ Phase ends when all pass with empty stack

## Success Criteria

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

## Dependencies

### From Previous Phases
- Phase 1: Event system, zones, cards
- Phase 1.5: Domain services, value objects
- Phase 2: Turn/phase management
- Phase 2.75: Automatic phase progression

### For Phase 4 (Future)
- Full spell casting (costs, targeting)
- Full ability system (triggered, activated, static)
- Card abilities implementation

## Risks and Mitigations

### Risk 1: Complex Priority Logic
**Mitigation**: Start simple, add complexity incrementally

### Risk 2: Stack Resolution Edge Cases
**Mitigation**: Follow rules exactly, test thoroughly

### Risk 3: Phase Integration Complexity
**Mitigation**: Integrate incrementally, test after each step

### Risk 4: Performance with Large Stack
**Mitigation**: Use efficient data structures, profile if needed

## Estimated Effort

- **Stack Implementation**: 4-6 hours
- **Priority System**: 4-6 hours
- **Stack Resolution**: 3-4 hours
- **Phase Integration**: 4-5 hours
- **Spell/Ability Foundation**: 3-4 hours
- **Testing**: 3-4 hours

**Total**: ~21-29 hours (approximately 1 week)

## Deliverables

1. ✅ `Stack` class fully implemented
2. ✅ `PriorityManager` class fully implemented
3. ✅ `StackResolver` class fully implemented
4. ✅ Phase integration complete
5. ✅ Stack and priority events
6. ✅ Foundation for spells and abilities
7. ✅ Updated console app demonstrating functionality
8. ✅ Documentation updated

## Next Steps After Phase 3

After completing Phase 3, we'll have:
- Working stack system
- Priority passing system
- Foundation for spell casting
- Foundation for ability activation
- Ready for Phase 4 (Card System and Abilities)

Phase 4 will build on this to add:
- Full spell casting with costs and targeting
- Complete ability system
- Triggered abilities
- Static abilities

## Notes

- This phase focuses on infrastructure (stack/priority)
- Full spell/ability implementation comes in Phase 4
- Foundation classes enable future features
- Keep it simple and working
- Follow rules exactly for priority and stack behavior
