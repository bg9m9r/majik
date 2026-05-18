# Phase 6: Rules Engine Enhancement - Implementation Plan

## Overview

Phase 6 enhances the rules engine with comprehensive validation, expands state-based actions (SBAs), and integrates rule checking throughout the game flow. This phase builds on the foundation established in previous phases, particularly the state-based actions system and the combat system from Phase 5.

**Status**: ⏳ Not Started  
**Goal**: Comprehensive rules validation, enhanced state-based actions, and action legality checking  
**Estimated Effort**: 1-2 weeks  
**Dependencies**: Phase 5 (Combat System), Phase 4 (Card System, State-Based Actions foundation)

## Objectives

1. **Enhanced State-Based Actions**: Implement all major SBAs (Legend rule, Planeswalker uniqueness, etc.)
2. **Rules Engine**: Create comprehensive RulesEngine for action validation
3. **Action Validation**: Validate player actions before execution
4. **SBA Integration**: Integrate SBA checking throughout game flow
5. **Zone Service Integration**: Properly use ZoneService for card movement in SBAs
6. **Comprehensive Testing**: Full test coverage for all rules and SBAs
7. **Documentation**: Document all rules and their implementations

## Magic Rules Reference

This implementation follows the official Magic: The Gathering Comprehensive Rules (2025-11-14):

- **Rule 704**: State-Based Actions
  - **704.1**: SBAs are checked whenever a player would receive priority
  - **704.5**: Player loses the game (0 or less life, empty library draw)
  - **704.5f**: Creature dies (damage >= toughness)
  - **704.5j**: Planeswalker dies (0 loyalty)
  - **704.5k**: Legend rule (multiple legendary permanents with same name)
  - **704.5m**: Planeswalker uniqueness rule (multiple planeswalkers with same subtype)
  - **704.5n**: World rule (multiple world enchantments)
  - **704.5p**: Token leaves battlefield (moved to non-battlefield zone)
  - **704.5q**: Copy leaves battlefield (moved to non-battlefield zone)
  - **704.5r**: Aura with no legal attachment
  - **704.5s**: Equipment with no legal attachment
  - **704.5t**: Fortification with no legal attachment
  - **704.5u**: Creature with 0 or less toughness
  - **704.5v**: Creature with 0 or less power (if applicable)

- **Rule 117**: Timing and Priority
  - **117.3**: Active player receives priority first
  - **117.4**: Stack must be empty and all players pass to advance phase

- **Rule 400-407**: Zone Rules
  - Zone transitions and restrictions
  - Zone change events

## Architecture Overview

### Rules System Components

```
Majik.Core/Rules/
├── StateBasedActions.cs          # Enhanced SBA checking (exists, needs expansion)
├── RulesEngine.cs                 # Comprehensive rules validation (new)
├── ActionValidator.cs             # Action legality checking (new)
└── RuleViolation.cs               # Rule violation exception/value object (new)
```

### Integration Points

- **StateBasedActions**: Already integrated with CombatManager, needs expansion
- **Game Aggregate**: Integrate SBA checking after each action
- **PhaseManager**: Check SBAs before phase transitions
- **PriorityManager**: Check SBAs before priority passes
- **StackResolver**: Check SBAs after each resolution
- **ZoneService**: Use for proper card movement in SBAs
- **EventBus**: Publish SBA execution events

## Detailed Implementation Plan

### Task 1: Enhance State-Based Actions

**Priority**: High  
**Estimated Time**: 2-3 days

#### 1.1 Complete Existing SBAs

**File**: `Majik.Core/Rules/StateBasedActions.cs`

**Current Implementation**:
- ✅ Player loses (0 or less life)
- ✅ Creature dies (damage >= toughness)
- ✅ Planeswalker dies (0 loyalty)
- ⏳ Legend rule
- ⏳ Planeswalker uniqueness rule
- ⏳ Proper zone movement

**Enhancements Needed**:

1. **Zone Service Integration**:
   ```csharp
   private readonly ZoneService? _zoneService;
   
   private void CheckCreatureDeath(IEnumerable<ICard> allCards)
   {
       // Use ZoneService to move creature to graveyard
       if (creature.IsDead() && creature.Zone == ZoneType.Battlefield)
       {
           _zoneService?.MoveCardTo(creature, ZoneType.Graveyard);
           _eventBus?.Publish(new StateBasedActionExecutedEvent(...));
       }
   }
   ```

2. **Legend Rule (Rule 704.5k)**:
   ```csharp
   private void CheckLegendRule(IEnumerable<ICard> allCards)
   {
       // Group legendary permanents by name and controller
       var legendaryGroups = allCards
           .OfType<Permanent>()
           .Where(p => p.HasSupertype(CardSupertype.Legendary))
           .Where(p => p.Zone == ZoneType.Battlefield)
           .GroupBy(p => new { p.Name, p.Controller });
       
       foreach (var group in legendaryGroups)
       {
           if (group.Count() > 1)
           {
               // Keep the one that entered first (or most recently, per rules)
               // Put others in graveyard
               var toKeep = group.OrderBy(p => p.Timestamp).First();
               foreach (var permanent in group.Where(p => p != toKeep))
               {
                   _zoneService?.MoveCardTo(permanent, ZoneType.Graveyard);
               }
           }
       }
   }
   ```

3. **Planeswalker Uniqueness Rule (Rule 704.5m)**:
   ```csharp
   private void CheckPlaneswalkerUniqueness(IEnumerable<ICard> allCards)
   {
       // Group planeswalkers by subtype and controller
       var planeswalkerGroups = allCards
           .OfType<Planeswalker>()
           .Where(p => p.Zone == ZoneType.Battlefield)
           .GroupBy(p => new { 
               Subtype = p.Subtypes.FirstOrDefault(s => s.Name == "Jace" || ...), 
               p.Controller 
           });
       
       foreach (var group in planeswalkerGroups)
       {
           if (group.Count() > 1)
           {
               // Keep one, put others in graveyard
               // Similar to legend rule
           }
       }
   }
   ```

4. **Additional SBAs** (if time permits):
   - World rule (Rule 704.5n)
   - Token/copy leaving battlefield (Rules 704.5p, 704.5q)
   - Aura/Equipment/Fortification with no legal attachment (Rules 704.5r, 704.5s, 704.5t)
   - Creature with 0 or less toughness (Rule 704.5u)

#### 1.2 SBA Execution Order

**Implementation**:
- SBAs must be checked in order (Rule 704.3)
- SBAs are checked repeatedly until none execute (Rule 704.4)
- Only one SBA executes per check (Rule 704.4)

**Code Structure**:
```csharp
public void CheckStateBasedActions(IEnumerable<Player> players, IEnumerable<ICard> allCards)
{
    bool anyExecuted;
    do
    {
        anyExecuted = false;
        
        // Check in order (Rule 704.3)
        if (CheckPlayerLife(players)) anyExecuted = true;
        if (CheckCreatureDeath(allCards)) anyExecuted = true;
        if (CheckPlaneswalkerDeath(allCards)) anyExecuted = true;
        if (CheckLegendRule(allCards)) anyExecuted = true;
        if (CheckPlaneswalkerUniqueness(allCards)) anyExecuted = true;
        // ... other SBAs
        
    } while (anyExecuted); // Repeat until no SBAs execute
}
```

### Task 2: Create Rules Engine

**Priority**: High  
**Estimated Time**: 2-3 days

#### 2.1 RulesEngine Class

**File**: `Majik.Core/Rules/RulesEngine.cs`

**Purpose**: Comprehensive rules validation service

**Key Methods**:
```csharp
public class RulesEngine
{
    /// <summary>
    /// Validate if a spell can be cast.
    /// </summary>
    public bool CanCastSpell(ICard card, Player player, bool isMainPhase, bool isStackEmpty);
    
    /// <summary>
    /// Validate if an ability can be activated.
    /// </summary>
    public bool CanActivateAbility(IActivatedAbility ability, Player player);
    
    /// <summary>
    /// Validate if a creature can attack.
    /// </summary>
    public bool CanAttack(Creature creature, Player activePlayer);
    
    /// <summary>
    /// Validate if a creature can block.
    /// </summary>
    public bool CanBlock(Creature creature, Attacker attacker, Player defendingPlayer);
    
    /// <summary>
    /// Validate zone transition.
    /// </summary>
    public bool CanMoveCard(ICard card, ZoneType fromZone, ZoneType toZone);
    
    /// <summary>
    /// Validate target selection.
    /// </summary>
    public bool IsValidTarget(ITarget target, TargetSpecification specification);
    
    /// <summary>
    /// Validate mana payment.
    /// </summary>
    public bool CanPayMana(Player player, ManaCost cost);
}
```

**Validation Rules**:
- Timing restrictions (instants vs sorceries)
- Zone restrictions (cast from hand, etc.)
- Targeting restrictions
- Cost payment validation
- State-based restrictions

#### 2.2 Integration with Existing Validators

**Strategy**: 
- RulesEngine delegates to specialized validators where they exist
- CombatValidator for combat actions
- TargetValidator for targeting
- CostValidator for costs
- RulesEngine coordinates and adds additional checks

### Task 3: Create Action Validator

**Priority**: Medium  
**Estimated Time**: 1-2 days

#### 3.1 ActionValidator Class

**File**: `Majik.Core/Rules/ActionValidator.cs`

**Purpose**: Validates player actions before execution

**Key Methods**:
```csharp
public class ActionValidator
{
    private readonly RulesEngine _rulesEngine;
    private readonly IEventBus? _eventBus;
    
    /// <summary>
    /// Validate a player action.
    /// </summary>
    public ValidationResult ValidateAction(PlayerAction action);
    
    /// <summary>
    /// Check if action is legal in current game state.
    /// </summary>
    public bool IsActionLegal(PlayerAction action, GameState gameState);
}
```

**Validation Result**:
```csharp
public class ValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }
    public RuleViolation? Violation { get; }
}
```

### Task 4: Integrate SBA Checking Throughout Game Flow

**Priority**: High  
**Estimated Time**: 1-2 days

#### 4.1 Integration Points

1. **After Each Event**:
   - In EventBus.Publish, check SBAs after event is published
   - Or in Game.Update() method

2. **After Stack Resolution**:
   - In StackResolver.ResolveTop(), check SBAs after resolution
   - Already partially done in CombatManager

3. **Before Phase Transitions**:
   - In PhaseManager.TransitionToNextPhase(), check SBAs before transition

4. **Before Priority Passes**:
   - In PriorityManager.PassPriority(), check SBAs before passing

5. **After Combat Damage**:
   - Already done in CombatManager.AssignCombatDamage()

#### 4.2 SBA Checking Service

**Option**: Create SBA checking wrapper that can be called from multiple places

```csharp
public class StateBasedActionChecker
{
    private readonly StateBasedActions _sba;
    private readonly Game _game;
    
    public void CheckAfterEvent(GameEvent evt)
    {
        var players = _game.Players;
        var allCards = GetAllCardsInGame();
        _sba.CheckStateBasedActions(players, allCards);
    }
    
    public void CheckAfterResolution()
    {
        // Same as above
    }
}
```

### Task 5: Enhance Card Properties for SBAs

**Priority**: Medium  
**Estimated Time**: 1 day

#### 5.1 Add Timestamp to Permanent

**Purpose**: Track when permanents entered battlefield for Legend rule

**File**: `Majik.Core/Cards/Permanent.cs`

```csharp
public class Permanent : Card
{
    public DateTime EnteredBattlefieldTimestamp { get; private set; }
    
    // Set when card enters battlefield
}
```

#### 5.2 Add Subtype Support for Planeswalkers

**Purpose**: Track planeswalker subtypes for uniqueness rule

**File**: `Majik.Core/Cards/Planeswalker.cs`

```csharp
// Already has Subtypes from Card base class
// Need to ensure subtypes are properly set (e.g., "Jace", "Liliana")
```

### Task 6: Unit Tests

**Priority**: High  
**Estimated Time**: 2-3 days

#### 6.1 Test Files to Create

1. **StateBasedActionsTests.cs** (enhance existing):
   - Test legend rule
   - Test planeswalker uniqueness
   - Test SBA execution order
   - Test repeated SBA checking
   - Test zone service integration

2. **RulesEngineTests.cs** (new):
   - Test spell casting validation
   - Test ability activation validation
   - Test combat validation
   - Test zone transition validation
   - Test targeting validation

3. **ActionValidatorTests.cs** (new):
   - Test action validation
   - Test validation results
   - Test rule violations

#### 6.2 Test Coverage Goals

- 100% coverage for StateBasedActions
- 90%+ coverage for RulesEngine
- 90%+ coverage for ActionValidator
- Edge cases for all SBAs
- Integration tests for SBA flow

### Task 7: Console App Testing

**Priority**: Medium  
**Estimated Time**: 1 day

#### 7.1 Test Scenarios

1. **Legend Rule Scenario**:
   - Create two legendary creatures with same name
   - Verify one is put in graveyard

2. **Planeswalker Uniqueness Scenario**:
   - Create two planeswalkers with same subtype
   - Verify one is put in graveyard

3. **SBA Execution Order**:
   - Create scenario where multiple SBAs should execute
   - Verify correct order and repeated checking

4. **Zone Service Integration**:
   - Verify creatures/planeswalkers properly move to graveyard
   - Verify events are published

## Implementation Order

1. **Week 1**:
   - Day 1-2: Enhance StateBasedActions (zone service, legend rule, planeswalker uniqueness)
   - Day 3-4: Create RulesEngine
   - Day 5: Create ActionValidator

2. **Week 2**:
   - Day 1-2: Integrate SBA checking throughout game flow
   - Day 3-4: Unit tests
   - Day 5: Console app testing and documentation

## Dependencies

- **Phase 4**: Card system, ZoneService, StateBasedActions foundation
- **Phase 5**: Combat system (for combat-related validation)
- **ZoneService**: Must be fully functional for SBA card movement

## Success Criteria

1. ✅ All major SBAs implemented (player loses, creature dies, planeswalker dies, legend rule, planeswalker uniqueness)
2. ✅ SBAs properly use ZoneService for card movement
3. ✅ RulesEngine validates all major actions
4. ✅ SBA checking integrated throughout game flow
5. ✅ 90%+ test coverage for rules system
6. ✅ All tests passing
7. ✅ Console app demonstrates SBA scenarios

## Risks and Mitigations

### Risk 1: SBA Execution Order Complexity
**Mitigation**: Follow Rule 704.3 exactly, test thoroughly

### Risk 2: Performance Impact of Frequent SBA Checking
**Mitigation**: Optimize SBA checking, only check when necessary

### Risk 3: Legend Rule Timestamp Tracking
**Mitigation**: Use simple timestamp, test edge cases

### Risk 4: Planeswalker Subtype Matching
**Mitigation**: Ensure subtypes are properly set on planeswalkers

## Testing Strategy

### Unit Tests
- Test each SBA in isolation
- Test SBA execution order
- Test repeated SBA checking
- Test RulesEngine validation methods
- Test ActionValidator

### Integration Tests
- Test SBA checking after events
- Test SBA checking after stack resolution
- Test SBA checking in combat
- Test legend rule with multiple permanents
- Test planeswalker uniqueness

### Scenario Tests
- Full game scenarios with SBAs
- Complex SBA interactions
- Edge cases

## Documentation

- Document each SBA with rule reference
- Document RulesEngine validation rules
- Document SBA execution order
- Document integration points

## Future Enhancements

- Additional SBAs (world rule, tokens, copies, attachments)
- Performance optimization
- SBA caching
- Rule violation reporting
- Comprehensive rule documentation
