# Phase 5: Combat System - Implementation Plan

## Overview

Phase 5 implements the complete combat system for Magic: The Gathering, including attacker/blocker declaration, damage calculation, combat abilities, and full integration with the existing game systems. This phase builds on the foundation established in Phases 1-4, particularly the creature system, state-based actions, and phase management.

**Status**: ⏳ Not Started  
**Goal**: Full combat implementation with all combat phases, damage resolution, and combat abilities  
**Estimated Effort**: 1-2 weeks  
**Dependencies**: Phase 4 (creatures, abilities, state-based actions)

## Objectives

1. **Combat Management**: Implement CombatManager to coordinate all combat operations
2. **Attacker Declaration**: Allow active player to declare attacking creatures
3. **Blocker Declaration**: Allow defending player to declare blocking creatures
4. **Damage Calculation**: Calculate and assign combat damage correctly
5. **Combat Abilities**: Handle first strike, double strike, trample, deathtouch, etc.
6. **Combat Events**: Fire appropriate events for all combat actions
7. **Integration**: Seamlessly integrate with existing phase, priority, and stack systems
8. **Unit Tests**: Comprehensive test coverage for all combat functionality

## Magic Rules Reference

This implementation follows the official Magic: The Gathering Comprehensive Rules (2025-11-14):

- **Rule 500.1**: Turn structure includes Combat Phase
- **Rule 506**: Combat Phase structure
- **Rule 507**: Beginning of Combat step
- **Rule 508**: Declare Attackers step
- **Rule 509**: Declare Blockers step
- **Rule 510**: Combat Damage step
- **Rule 511**: End of Combat step
- **Rule 702**: Keyword abilities (first strike, trample, etc.)

## Architecture Overview

### Combat System Components

```
Majik.Core/Combat/
├── CombatManager.cs          # Main combat coordinator
├── Combat.cs                  # Combat instance (value object/entity)
├── Attacker.cs                # Attacking creature wrapper
├── Blocker.cs                 # Blocking creature wrapper
├── CombatDamage.cs            # Damage assignment value object
├── CombatValidator.cs         # Validates combat actions
└── CombatAbilities.cs         # Combat ability helpers
```

### Integration Points

- **PhaseManager**: Combat phases already exist, need to integrate combat logic
- **PriorityManager**: Players need priority during combat steps
- **Stack**: Combat abilities can be activated/triggered
- **StateBasedActions**: Creatures die from combat damage
- **ZoneService**: Creatures move to graveyard when they die
- **EventBus**: Combat events for UI/observers

## Detailed Implementation Plan

### Task 1: Combat Value Objects and Entities

**Priority**: High  
**Estimated Time**: 1 day

#### 1.1 Combat Value Object

**File**: `Majik.Core/Combat/Combat.cs`

**Purpose**: Immutable value object representing a combat instance

**Properties**:
```csharp
public class Combat
{
    public Player AttackingPlayer { get; }
    public Player? DefendingPlayer { get; }
    public IReadOnlyList<Attacker> Attackers { get; }
    public DateTime Timestamp { get; }
    public CombatState State { get; }
}
```

**Features**:
- Immutable combat state
- Tracks attacking and defending players
- Lists all attackers
- Tracks combat state (DeclaringAttackers, DeclaringBlockers, AssigningDamage, Resolved)

**Validation**:
- Attacking player must be active player
- Defending player must be opponent (or planeswalker)
- Attackers must be valid creatures

#### 1.2 Attacker Entity

**File**: `Majik.Core/Combat/Attacker.cs`

**Purpose**: Represents an attacking creature

**Properties**:
```csharp
public class Attacker
{
    public Creature Creature { get; }
    public Player? TargetPlayer { get; }
    public Planeswalker? TargetPlaneswalker { get; }
    public IReadOnlyList<Blocker> Blockers { get; }
    public int AssignedDamage { get; private set; }
    public bool HasFirstStrike { get; }
    public bool HasDoubleStrike { get; }
    public bool HasTrample { get; }
    public bool HasDeathtouch { get; }
}
```

**Features**:
- Wraps a creature that is attacking
- Tracks target (player or planeswalker)
- Tracks assigned blockers
- Tracks damage assignment
- Tracks combat abilities

**Methods**:
- `AssignDamage(int amount)`: Assign damage to this attacker
- `CanAttack()`: Check if creature can attack
- `GetCombatAbilities()`: Get relevant combat abilities

#### 1.3 Blocker Entity

**File**: `Majik.Core/Combat/Blocker.cs`

**Purpose**: Represents a blocking creature

**Properties**:
```csharp
public class Blocker
{
    public Creature Creature { get; }
    public Attacker BlockedAttacker { get; }
    public int AssignedDamage { get; private set; }
    public bool HasFirstStrike { get; }
    public bool HasDoubleStrike { get; }
    public bool HasDeathtouch { get; }
}
```

**Features**:
- Wraps a creature that is blocking
- References the attacker being blocked
- Tracks damage assignment
- Tracks combat abilities

**Methods**:
- `AssignDamage(int amount)`: Assign damage to this blocker
- `CanBlock(Attacker attacker)`: Check if creature can block attacker
- `GetCombatAbilities()`: Get relevant combat abilities

#### 1.4 CombatDamage Value Object

**File**: `Majik.Core/Combat/CombatDamage.cs`

**Purpose**: Immutable value object representing damage assignment

**Properties**:
```csharp
public class CombatDamage : IEquatable<CombatDamage>
{
    public Creature Source { get; }
    public ICard? Target { get; } // Creature, Player, or Planeswalker
    public int Amount { get; }
    public bool IsCombatDamage { get; }
    public bool IsLethal { get; }
}
```

**Features**:
- Immutable damage representation
- Tracks source and target
- Indicates if damage is lethal
- Equality comparison

**Unit Tests**: `Majik.Core.Tests/Combat/CombatDamageTests.cs`
- Test creation and validation
- Test equality
- Test lethal damage calculation

### Task 2: Combat Validator

**Priority**: High  
**Estimated Time**: 1 day

#### 2.1 CombatValidator Service

**File**: `Majik.Core/Combat/CombatValidator.cs`

**Purpose**: Validates combat actions according to Magic rules

**Methods**:
```csharp
public class CombatValidator
{
    public bool CanAttack(Creature creature, Player activePlayer);
    public bool CanBlock(Creature creature, Attacker attacker);
    public bool CanAttackPlayer(Player target, Player attacker);
    public bool CanAttackPlaneswalker(Planeswalker target, Player attacker);
    public bool IsValidAttackDeclaration(IEnumerable<Creature> attackers, Player activePlayer);
    public bool IsValidBlockDeclaration(Creature blocker, Attacker attacker);
}
```

**Validation Rules** (Rule 508, 509):
- Creature must be untapped (unless has vigilance)
- Creature must not have summoning sickness (unless has haste)
- Creature must be controlled by active player (for attacking)
- Creature must be on battlefield
- Can only attack once per combat
- Can only block once per combat
- Blocking creature must be untapped
- Blocking creature must be controlled by defending player

**Unit Tests**: `Majik.Core.Tests/Combat/CombatValidatorTests.cs`
- Test attack validation (tapped, summoning sickness, control)
- Test block validation (tapped, control, already blocking)
- Test target validation (player, planeswalker)
- Test edge cases (vigilance, haste)

### Task 3: Combat Manager

**Priority**: High  
**Estimated Time**: 2-3 days

#### 3.1 CombatManager Service

**File**: `Majik.Core/Combat/CombatManager.cs`

**Purpose**: Main service coordinating all combat operations

**Properties**:
```csharp
public class CombatManager
{
    public Combat? CurrentCombat { get; }
    public bool IsInCombat => CurrentCombat != null;
}
```

**Methods**:
```csharp
public class CombatManager
{
    // Combat initialization
    public void StartCombat(Player activePlayer);
    
    // Attacker declaration
    public void DeclareAttackers(Player activePlayer, IEnumerable<AttackerDeclaration> declarations);
    
    // Blocker declaration
    public void DeclareBlockers(Player defendingPlayer, IEnumerable<BlockerDeclaration> declarations);
    
    // Damage assignment
    public void AssignCombatDamage();
    
    // Damage resolution
    public void ResolveCombatDamage();
    
    // Combat cleanup
    public void EndCombat();
    
    // Helper methods
    public IEnumerable<Creature> GetValidAttackers(Player player);
    public IEnumerable<Creature> GetValidBlockers(Player player, Attacker attacker);
}
```

**Combat Flow** (Rule 506-511):

1. **Beginning of Combat Step** (Rule 507):
   - Triggers fire
   - Players receive priority
   - No attackers declared yet

2. **Declare Attackers Step** (Rule 508):
   - Active player declares attackers
   - Choose targets (player or planeswalker)
   - Attackers become tapped (unless vigilance)
   - Triggers fire
   - Players receive priority

3. **Declare Blockers Step** (Rule 509):
   - Defending player declares blockers
   - Each blocker blocks one attacker
   - Multiple blockers can block same attacker
   - Triggers fire
   - Players receive priority

4. **Combat Damage Step** (Rule 510):
   - First strike damage (if applicable)
   - Regular damage
   - Damage assignment order
   - Damage dealt
   - State-based actions checked

5. **End of Combat Step** (Rule 511):
   - End of combat triggers fire
   - Combat ends
   - Players receive priority

**Integration**:
- Uses `CombatValidator` for validation
- Publishes combat events via `IEventBus`
- Integrates with `StateBasedActions` for creature death
- Uses `ZoneService` for moving creatures to graveyard

**Unit Tests**: `Majik.Core.Tests/Combat/CombatManagerTests.cs`
- Test combat initialization
- Test attacker declaration (valid/invalid)
- Test blocker declaration (valid/invalid)
- Test damage assignment
- Test damage resolution
- Test combat cleanup
- Test combat events

### Task 4: Combat Abilities

**Priority**: High  
**Estimated Time**: 2 days

#### 4.1 Combat Ability Helpers

**File**: `Majik.Core/Combat/CombatAbilities.cs`

**Purpose**: Helper methods for combat-related abilities

**Methods**:
```csharp
public static class CombatAbilities
{
    public static bool HasFirstStrike(Creature creature);
    public static bool HasDoubleStrike(Creature creature);
    public static bool HasTrample(Creature creature);
    public static bool HasDeathtouch(Creature creature);
    public static bool HasVigilance(Creature creature);
    public static bool HasHaste(Creature creature);
    public static bool HasReach(Creature creature);
    public static bool HasFlying(Creature creature);
}
```

**Combat Abilities** (Rule 702):

- **First Strike** (702.7): Deals damage in first strike combat damage step
- **Double Strike** (702.4): Deals damage in both first strike and regular damage steps
- **Trample** (702.19): Excess damage to blocking creature goes to defending player/planeswalker
- **Deathtouch** (702.2): Any amount of damage is lethal
- **Vigilance** (702.20): Attacking doesn't cause creature to tap
- **Haste** (702.10): Can attack/activate abilities without waiting for summoning sickness
- **Reach** (702.17): Can block creatures with flying
- **Flying** (702.9): Can only be blocked by creatures with flying or reach

**Implementation Notes**:
- Initially, check for abilities on creature (static abilities)
- Future: Support for granted abilities, temporary abilities
- Use `StaticAbilityManager` to check for active abilities

**Unit Tests**: `Majik.Core.Tests/Combat/CombatAbilitiesTests.cs`
- Test each combat ability check
- Test ability combinations
- Test edge cases

### Task 5: Damage Assignment and Resolution

**Priority**: High  
**Estimated Time**: 2-3 days

#### 5.1 Damage Assignment Logic

**Location**: `CombatManager.AssignCombatDamage()`

**Purpose**: Assign damage according to Magic rules (Rule 510)

**Damage Assignment Rules**:

1. **First Strike Damage Step** (if applicable):
   - Only creatures with first strike or double strike deal damage
   - Damage is assigned and dealt
   - State-based actions checked

2. **Regular Damage Step**:
   - All creatures that didn't deal first strike damage deal damage
   - Creatures with double strike deal damage again
   - Damage is assigned and dealt
   - State-based actions checked

3. **Damage Assignment Order** (Rule 510.1):
   - Attacker assigns damage to blockers
   - If multiple blockers, attacker chooses order
   - Must assign lethal damage to first blocker before assigning to next
   - Trample: Excess damage can be assigned to defending player/planeswalker

4. **Trample Damage** (Rule 702.19):
   - Attacker must assign lethal damage to all blockers
   - Excess damage can be assigned to defending player/planeswalker
   - Deathtouch: 1 damage is lethal

**Implementation**:
```csharp
private void AssignCombatDamage()
{
    var combat = CurrentCombat;
    if (combat == null) return;
    
    // First strike damage step
    if (HasFirstStrikeDamage(combat))
    {
        AssignFirstStrikeDamage(combat);
        ResolveDamage(combat, isFirstStrike: true);
        _stateBasedActions?.CheckStateBasedActions(...);
    }
    
    // Regular damage step
    AssignRegularDamage(combat);
    ResolveDamage(combat, isFirstStrike: false);
    _stateBasedActions?.CheckStateBasedActions(...);
}

private void AssignFirstStrikeDamage(Combat combat)
{
    foreach (var attacker in combat.Attackers)
    {
        if (attacker.HasFirstStrike || attacker.HasDoubleStrike)
        {
            AssignAttackerDamage(attacker);
        }
    }
}

private void AssignRegularDamage(Combat combat)
{
    foreach (var attacker in combat.Attackers)
    {
        if (!attacker.HasFirstStrike || attacker.HasDoubleStrike)
        {
            AssignAttackerDamage(attacker);
        }
    }
}

private void AssignAttackerDamage(Attacker attacker)
{
    if (attacker.Blockers.Count == 0)
    {
        // Unblocked: all damage to target
        attacker.AssignDamage(attacker.Creature.Power);
        return;
    }
    
    // Blocked: assign damage to blockers
    int remainingPower = attacker.Creature.Power;
    
    foreach (var blocker in attacker.Blockers)
    {
        int lethalDamage = CalculateLethalDamage(blocker.Creature, attacker.HasDeathtouch);
        int assignedDamage = Math.Min(lethalDamage, remainingPower);
        
        blocker.AssignDamage(assignedDamage);
        attacker.AssignDamage(assignedDamage);
        remainingPower -= assignedDamage;
        
        if (remainingPower <= 0) break;
    }
    
    // Trample: excess damage to target
    if (attacker.HasTrample && remainingPower > 0)
    {
        attacker.AssignDamage(remainingPower);
    }
}

private int CalculateLethalDamage(Creature creature, bool hasDeathtouch)
{
    if (hasDeathtouch) return 1;
    return creature.Toughness;
}
```

#### 5.2 Damage Resolution

**Location**: `CombatManager.ResolveCombatDamage()`

**Purpose**: Apply damage to creatures, players, and planeswalkers

**Implementation**:
```csharp
private void ResolveCombatDamage(Combat combat, bool isFirstStrike)
{
    foreach (var attacker in combat.Attackers)
    {
        // Deal damage to blockers
        foreach (var blocker in attacker.Blockers)
        {
            if (blocker.AssignedDamage > 0)
            {
                blocker.Creature.TakeDamage(blocker.AssignedDamage);
                _eventBus?.Publish(new CombatDamageDealtEvent(
                    attacker.Creature, blocker.Creature, blocker.AssignedDamage, isFirstStrike));
            }
        }
        
        // Deal damage to target (unblocked or trample)
        int targetDamage = attacker.AssignedDamage - 
            attacker.Blockers.Sum(b => b.AssignedDamage);
        
        if (targetDamage > 0)
        {
            if (attacker.TargetPlayer != null)
            {
                attacker.TargetPlayer.LoseLife(targetDamage);
                _eventBus?.Publish(new CombatDamageDealtEvent(
                    attacker.Creature, attacker.TargetPlayer, targetDamage, isFirstStrike));
            }
            else if (attacker.TargetPlaneswalker != null)
            {
                attacker.TargetPlaneswalker.RemoveLoyalty(targetDamage);
                _eventBus?.Publish(new CombatDamageDealtEvent(
                    attacker.Creature, attacker.TargetPlaneswalker, targetDamage, isFirstStrike));
            }
        }
        
        // Deal damage to attacker from blockers
        foreach (var blocker in attacker.Blockers)
        {
            if (blocker.AssignedDamage > 0)
            {
                attacker.Creature.TakeDamage(blocker.Creature.Power);
                _eventBus?.Publish(new CombatDamageDealtEvent(
                    blocker.Creature, attacker.Creature, blocker.Creature.Power, isFirstStrike));
            }
        }
    }
}
```

**Unit Tests**: `Majik.Core.Tests/Combat/CombatDamageTests.cs`
- Test first strike damage assignment
- Test regular damage assignment
- Test trample damage
- Test deathtouch damage
- Test multiple blockers
- Test unblocked attackers
- Test damage resolution

### Task 6: Combat Events

**Priority**: Medium  
**Estimated Time**: 1 day

#### 6.1 Combat Domain Events

**Files**: 
- `Majik.Core/Domain/DomainEvents/CombatStartedEvent.cs`
- `Majik.Core/Domain/DomainEvents/AttackersDeclaredEvent.cs`
- `Majik.Core/Domain/DomainEvents/BlockersDeclaredEvent.cs`
- `Majik.Core/Domain/DomainEvents/CombatDamageDealtEvent.cs`
- `Majik.Core/Domain/DomainEvents/CombatEndedEvent.cs`

**Purpose**: Domain events for all combat actions

**Event Properties**:

```csharp
public class CombatStartedEvent : GameEvent
{
    public Player ActivePlayer { get; }
    public DateTime Timestamp { get; }
}

public class AttackersDeclaredEvent : GameEvent
{
    public Combat Combat { get; }
    public IReadOnlyList<Attacker> Attackers { get; }
}

public class BlockersDeclaredEvent : GameEvent
{
    public Combat Combat { get; }
    public IReadOnlyList<Blocker> Blockers { get; }
}

public class CombatDamageDealtEvent : GameEvent
{
    public Creature Source { get; }
    public ICard? Target { get; } // Creature, Player, or Planeswalker
    public int Amount { get; }
    public bool IsFirstStrike { get; }
}

public class CombatEndedEvent : GameEvent
{
    public Combat Combat { get; }
}
```

**Integration**:
- Add to `EventType.cs` enum
- Publish from `CombatManager`
- Subscribe in console app for testing

**Unit Tests**: `Majik.Core.Tests/Combat/CombatEventsTests.cs`
- Test event publishing
- Test event properties
- Test event timing

### Task 7: Phase Integration

**Priority**: High  
**Estimated Time**: 1 day

#### 7.1 Integrate Combat with PhaseManager

**Location**: `Majik.Core/Game/PhaseManager.cs`

**Purpose**: Connect combat phases with combat logic

**Changes**:
- Add `CombatManager` to `PhaseManager`
- Call combat methods during combat phases
- Handle combat step transitions

**Implementation**:
```csharp
public class PhaseManager
{
    private readonly CombatManager _combatManager;
    
    private void ProcessCombatPhase(PhaseStateType phase)
    {
        switch (phase)
        {
            case PhaseStateType.BeginningOfCombat:
                _combatManager.StartCombat(_activePlayer);
                break;
                
            case PhaseStateType.DeclareAttackers:
                // Wait for player to declare attackers
                // CombatManager.DeclareAttackers() called by game/player action
                break;
                
            case PhaseStateType.DeclareBlockers:
                // Wait for player to declare blockers
                // CombatManager.DeclareBlockers() called by game/player action
                break;
                
            case PhaseStateType.CombatDamage:
                _combatManager.AssignCombatDamage();
                _combatManager.ResolveCombatDamage();
                break;
                
            case PhaseStateType.EndOfCombat:
                _combatManager.EndCombat();
                break;
        }
    }
}
```

**Integration Points**:
- `Game.cs`: Expose combat methods to players
- `PriorityManager`: Ensure priority during combat steps
- `StateBasedActions`: Check after combat damage

**Unit Tests**: `Majik.Core.Tests/Game/PhaseManagerCombatTests.cs`
- Test combat phase transitions
- Test combat integration with phases
- Test combat with priority passing

### Task 8: Game Integration

**Priority**: High  
**Estimated Time**: 1 day

#### 8.1 Add Combat to Game Aggregate

**Location**: `Majik.Core/Domain/Aggregates/Game.cs`

**Purpose**: Expose combat functionality to players

**Changes**:
- Add `CombatManager` to `Game`
- Add methods for declaring attackers/blockers
- Integrate with existing systems

**Methods**:
```csharp
public class Game
{
    public CombatManager CombatManager { get; }
    
    public void DeclareAttackers(Player player, IEnumerable<AttackerDeclaration> declarations)
    {
        if (player != ActivePlayer)
            throw new InvalidPlayerActionException("Only active player can declare attackers");
        
        if (PhaseManager.CurrentPhase != PhaseStateType.DeclareAttackers)
            throw new InvalidGameStateException("Not in declare attackers step");
        
        CombatManager.DeclareAttackers(player, declarations);
    }
    
    public void DeclareBlockers(Player player, IEnumerable<BlockerDeclaration> declarations)
    {
        if (player == ActivePlayer)
            throw new InvalidPlayerActionException("Active player cannot declare blockers");
        
        if (PhaseManager.CurrentPhase != PhaseStateType.DeclareBlockers)
            throw new InvalidGameStateException("Not in declare blockers step");
        
        CombatManager.DeclareBlockers(player, declarations);
    }
}
```

**Unit Tests**: `Majik.Core.Tests/Domain/Aggregates/GameCombatTests.cs`
- Test attacker declaration through Game
- Test blocker declaration through Game
- Test validation errors
- Test combat flow through Game

### Task 9: Console App Testing

**Priority**: Medium  
**Estimated Time**: 1 day

#### 9.1 Update Console App

**Location**: `Majik.Console/Program.cs`

**Purpose**: Test combat functionality

**Test Scenarios**:
1. **Basic Combat**: Simple attack with no blockers
2. **Blocked Combat**: Attack with blockers
3. **Multiple Attackers**: Multiple creatures attacking
4. **Multiple Blockers**: Multiple creatures blocking one attacker
5. **First Strike**: Test first strike combat
6. **Trample**: Test trample damage
7. **Deathtouch**: Test deathtouch ability
8. **Vigilance**: Test vigilance (untapped after attack)
9. **Combat Abilities**: Test various combat abilities
10. **Combat Events**: Verify all combat events fire

**Implementation**:
- Add combat test methods
- Subscribe to combat events
- Create test creatures with combat abilities
- Demonstrate full combat flow

### Task 10: Unit Tests

**Priority**: High  
**Estimated Time**: 2-3 days

#### 10.1 Test Files to Create

1. **`Majik.Core.Tests/Combat/CombatTests.cs`**
   - Test Combat value object
   - Test creation and validation
   - Test equality

2. **`Majik.Core.Tests/Combat/AttackerTests.cs`**
   - Test Attacker entity
   - Test damage assignment
   - Test combat abilities

3. **`Majik.Core.Tests/Combat/BlockerTests.cs`**
   - Test Blocker entity
   - Test damage assignment
   - Test combat abilities

4. **`Majik.Core.Tests/Combat/CombatDamageTests.cs`**
   - Test CombatDamage value object
   - Test damage calculation
   - Test lethal damage

5. **`Majik.Core.Tests/Combat/CombatValidatorTests.cs`**
   - Test attack validation
   - Test block validation
   - Test edge cases

6. **`Majik.Core.Tests/Combat/CombatManagerTests.cs`**
   - Test combat initialization
   - Test attacker declaration
   - Test blocker declaration
   - Test damage assignment
   - Test damage resolution
   - Test combat cleanup

7. **`Majik.Core.Tests/Combat/CombatAbilitiesTests.cs`**
   - Test each combat ability
   - Test ability combinations
   - Test edge cases

8. **`Majik.Core.Tests/Combat/CombatEventsTests.cs`**
   - Test event publishing
   - Test event properties

9. **`Majik.Core.Tests/Game/PhaseManagerCombatTests.cs`**
   - Test combat phase integration
   - Test phase transitions with combat

10. **`Majik.Core.Tests/Domain/Aggregates/GameCombatTests.cs`**
    - Test combat through Game aggregate
    - Test validation
    - Test integration

**Test Coverage Goals**:
- **CombatManager**: 100% coverage
- **CombatValidator**: 100% coverage
- **Combat value objects**: 100% coverage
- **Combat entities**: 95%+ coverage
- **Combat abilities**: 95%+ coverage
- **Overall combat system**: 90%+ coverage

**Test Patterns**:
- Use AAA pattern (Arrange-Act-Assert)
- Test happy paths
- Test error cases
- Test edge cases
- Use FluentAssertions
- Mock dependencies (EventBus, StateBasedActions, etc.)

## Implementation Order

1. **Task 1**: Combat Value Objects and Entities (Foundation)
2. **Task 2**: Combat Validator (Validation logic)
3. **Task 4**: Combat Abilities (Helper methods)
4. **Task 3**: Combat Manager (Core logic)
5. **Task 5**: Damage Assignment and Resolution (Complex logic)
6. **Task 6**: Combat Events (Event system)
7. **Task 7**: Phase Integration (Integration)
8. **Task 8**: Game Integration (API)
9. **Task 10**: Unit Tests (Testing)
10. **Task 9**: Console App Testing (Integration testing)

## Dependencies

### From Previous Phases
- ✅ **Phase 1**: Event system, zones, basic cards
- ✅ **Phase 1.5**: Domain services, value objects
- ✅ **Phase 2**: Turn/phase management (combat phases exist)
- ✅ **Phase 2.75**: Automatic phase progression
- ✅ **Phase 3**: Stack and priority system
- ✅ **Phase 3.5**: DDD refactoring
- ✅ **Phase 4**: Creatures, abilities, state-based actions

### Required for Phase 5
- ✅ Creature class with Power, Toughness, Damage
- ✅ StateBasedActions for creature death
- ✅ ZoneService for moving creatures to graveyard
- ✅ EventBus for combat events
- ✅ PhaseManager with combat phases
- ✅ PriorityManager for combat steps

## Success Criteria

### Functional Requirements
- ✅ Players can declare attackers during declare attackers step
- ✅ Players can declare blockers during declare blockers step
- ✅ Combat damage is calculated correctly
- ✅ First strike and double strike work correctly
- ✅ Trample damage works correctly
- ✅ Deathtouch works correctly
- ✅ Creatures die from combat damage (via state-based actions)
- ✅ All combat events fire correctly
- ✅ Combat integrates with phase system
- ✅ Combat integrates with priority system

### Technical Requirements
- ✅ CombatManager implemented
- ✅ Combat value objects implemented
- ✅ Combat entities implemented
- ✅ CombatValidator implemented
- ✅ Combat abilities helpers implemented
- ✅ All combat events implemented
- ✅ Integration with PhaseManager
- ✅ Integration with Game aggregate
- ✅ Comprehensive unit tests (90%+ coverage)
- ✅ Console app demonstrates combat

### Quality Requirements
- ✅ All code follows DDD principles
- ✅ All code follows existing patterns
- ✅ All code is well-documented
- ✅ All tests pass
- ✅ 0 compilation errors
- ✅ 0 warnings

## Risks and Mitigations

### Risk 1: Complexity of Damage Assignment
**Risk**: Damage assignment with multiple blockers and trample is complex  
**Mitigation**: 
- Start with simple cases (one attacker, one blocker)
- Build up to complex cases incrementally
- Create comprehensive test cases
- Reference Magic rules frequently

### Risk 2: Combat Ability Interactions
**Risk**: Combat abilities interact in complex ways  
**Mitigation**:
- Implement abilities one at a time
- Test each ability in isolation
- Test ability combinations
- Reference Magic rules for interactions

### Risk 3: Integration with Existing Systems
**Risk**: Combat must integrate with many existing systems  
**Mitigation**:
- Design clear interfaces
- Use dependency injection
- Test integration points thoroughly
- Follow existing patterns

### Risk 4: State-Based Actions Timing
**Risk**: State-based actions must be checked at correct times  
**Mitigation**:
- Check state-based actions after each damage step
- Test creature death scenarios
- Verify creatures move to graveyard correctly

## Testing Strategy

### Unit Tests
- Test each component in isolation
- Mock dependencies
- Test edge cases
- Test error conditions
- Test validation logic

### Integration Tests
- Test combat flow through Game
- Test combat with phase transitions
- Test combat with priority passing
- Test combat with state-based actions

### Scenario Tests
- Test specific combat scenarios
- Test combat ability combinations
- Test edge cases from Magic rules
- Test complex combat situations

## Documentation

### Code Documentation
- XML comments on all public members
- Inline comments for complex logic
- Reference Magic rule numbers
- Document design decisions

### Test Documentation
- Descriptive test names
- Test comments explaining scenarios
- Reference Magic rule numbers in tests

## Future Enhancements

### Phase 5.5 (Future)
- Combat damage prevention/replacement
- Combat triggers (when attacks, when blocks, etc.)
- Combat static abilities (affecting combat)
- Combat activated abilities
- Menace, hexproof, protection abilities
- More complex combat scenarios

## Notes

- Combat is a core part of Magic gameplay
- Must follow Magic rules exactly
- Integration with existing systems is critical
- Comprehensive testing is essential
- Reference Magic Comprehensive Rules frequently
- Maintain DDD principles throughout

## Estimated Timeline

- **Task 1**: 1 day
- **Task 2**: 1 day
- **Task 3**: 2-3 days
- **Task 4**: 2 days
- **Task 5**: 2-3 days
- **Task 6**: 1 day
- **Task 7**: 1 day
- **Task 8**: 1 day
- **Task 9**: 1 day
- **Task 10**: 2-3 days

**Total**: 14-17 days (2-3 weeks)

## Conclusion

Phase 5 implements the complete combat system for Magic: The Gathering, building on the solid foundation established in previous phases. The implementation follows Magic rules closely, integrates seamlessly with existing systems, and includes comprehensive unit tests to ensure correctness and maintainability.
