# Phase 2.75: Automatic Phase Progression

## Overview

Phase 2.75 fixes the issue where turns don't automatically progress through phases. Currently, phases must be manually advanced, but in Magic: The Gathering, a turn should automatically progress through all phases, with each phase handling its own completion logic.

## Problem Statement

**Current Behavior**:
- Turn starts with first phase
- Phases must be manually advanced via `AdvancePhase()` calls
- Console app has to loop through phases manually
- No automatic progression

**Expected Behavior**:
- Turn starts → automatically progresses through all phases
- Each phase executes its logic and automatically transitions
- Phases can be skipped if game state requires it
- Extra phases are automatically included
- Turn completes automatically after all phases

## Root Cause Analysis

1. **Phases Don't Auto-Complete**: Phases start but don't automatically transition
2. **No Phase Execution Logic**: Phase behaviors exist but aren't executed
3. **Manual Advancement Required**: `AdvancePhase()` must be called manually
4. **Missing Phase Lifecycle**: Phases don't have a "complete" state

## Solution Design

### Approach: Phase Execution and Auto-Transition

Each phase should:
1. **Enter**: Fire events, initialize
2. **Execute**: Run phase-specific logic (untap, draw, etc.)
3. **Complete**: Automatically transition to next phase (or wait for player input in future)

### Phase Execution Model

```
Turn Starts
  ↓
Phase 1 (Untap)
  → Execute: Untap all permanents
  → Auto-transition to next phase
  ↓
Phase 2 (Upkeep)
  → Execute: Upkeep triggers
  → Auto-transition to next phase
  ↓
... (continues through all phases)
  ↓
Turn Complete
```

## Implementation Plan

### Task 1: Phase Execution Interface

**Create**: `IPhaseExecutor` interface
- `Execute()`: Run phase logic
- `CanComplete()`: Check if phase can transition
- `OnComplete()`: Called when phase completes

**Purpose**: Standardize phase execution

### Task 2: Phase Behavior Execution

**Update Phase Classes**:
- `UntapStep`: Execute untap logic
- `DrawStep`: Execute draw logic
- `MainPhase`: Mark as "waiting for player" (for future)
- `EndStep`: Execute end step logic
- `CleanupStep`: Execute cleanup logic

**Each Phase Should**:
- Execute its logic in `OnEnter()` or separate `Execute()` method
- Automatically mark itself as complete
- Trigger transition to next phase

### Task 3: Automatic Phase Progression

**Update PhaseManager**:
- Add `ExecuteCurrentPhase()` method
- Add `AutoAdvance()` method that executes and advances
- Phases automatically transition after execution

**Update Game**:
- `AdvanceTurn()` should automatically process all phases
- Or add `ProcessTurn()` that runs complete turn cycle
- Phases execute and auto-advance

### Task 4: Phase Completion Logic

**Phase States**:
- `NotStarted`: Phase hasn't begun
- `Executing`: Phase is running
- `WaitingForPlayer`: Phase needs player input (future)
- `Complete`: Phase is done, can transition

**PhaseManager Should**:
- Track phase completion state
- Auto-advance when phase completes
- Handle phase skipping

### Task 5: Integration with Turn System

**TurnManager Integration**:
- When turn starts, automatically process all phases
- `StartTurn()` should trigger phase execution
- Turn completes when all phases are done

**Game Integration**:
- `AdvanceTurn()` automatically processes all phases
- No manual `AdvancePhase()` calls needed
- Turn is self-contained

## Detailed Implementation

### Phase Execution Flow

```csharp
public class PhaseManager
{
    public void ExecuteCurrentPhase()
    {
        if (_currentPhase == null) return;
        
        // Get phase executor
        var executor = GetPhaseExecutor(_currentPhase.Value);
        
        // Execute phase logic
        executor?.Execute();
        
        // Auto-advance if phase can complete
        if (executor?.CanComplete() == true)
        {
            TransitionToNextPhase();
        }
    }
    
    public void ProcessAllPhases()
    {
        while (!IsTurnComplete())
        {
            ExecuteCurrentPhase();
            
            // If phase didn't auto-advance, break (waiting for player)
            if (_currentPhase != null && !CanAutoAdvance())
            {
                break;
            }
        }
    }
}
```

### Phase Executor Pattern

```csharp
public interface IPhaseExecutor
{
    void Execute();
    bool CanComplete();
    void OnComplete();
}

public class UntapStepExecutor : IPhaseExecutor
{
    public void Execute()
    {
        // Untap all permanents
    }
    
    public bool CanComplete() => true; // Always auto-complete
    
    public void OnComplete() { }
}
```

### Turn Auto-Processing

```csharp
public void AdvanceTurn()
{
    // End current turn
    _turnManager.EndTurn();
    
    // Start next turn
    _turnManager.StartNextTurn();
    
    // Initialize phases
    _phaseManager.InitializeForTurn(...);
    _phaseManager.StartFirstPhase();
    
    // Automatically process all phases
    _phaseManager.ProcessAllPhases();
}
```

## Phase-Specific Behaviors

### Automatic Phases (Auto-Complete)
- **Untap**: Untap permanents → auto-complete
- **Upkeep**: Run triggers → auto-complete
- **Draw**: Draw card → auto-complete
- **End**: Run triggers → auto-complete
- **Cleanup**: Discard, cleanup → auto-complete

### Player-Controlled Phases (Future)
- **Main Phase**: Wait for player actions (stack/priority)
- **Combat**: Wait for player decisions (attackers, blockers)

For now, these can auto-complete, but structure should support waiting.

## Phase Skipping Logic

**Skip Conditions**:
- First turn: Skip draw step
- No permanents: Skip untap (optional)
- Game rules: Skip phases based on effects

**Implementation**:
- Check skip conditions before starting phase
- If skipped, immediately transition to next
- Fire skip events

## Extra Phases Integration

**Extra Phases Should**:
- Be inserted into sequence automatically
- Execute same as normal phases
- Not require manual intervention

**Current**: Extra phases are queued
**After**: Extra phases execute automatically when reached

## Testing Strategy

1. **Turn Auto-Processing**: Turn should complete all phases automatically
2. **Phase Execution**: Each phase should execute its logic
3. **Phase Skipping**: First turn should skip draw
4. **Extra Phases**: Extra phases should execute automatically
5. **Event Firing**: All phase events should fire correctly

## Success Criteria

- ✅ Turn automatically progresses through all phases
- ✅ Each phase executes its logic
- ✅ Phases auto-transition when complete
- ✅ First turn skips draw step
- ✅ Extra phases execute automatically
- ✅ No manual `AdvancePhase()` calls needed
- ✅ Console app can just call `AdvanceTurn()` and see full turn

## Files to Modify

### Core Phase System
- `Game/PhaseManager.cs`: Add execution and auto-advance logic
- `Game/Phases/*.cs`: Add execution logic to each phase
- `Game/TurnManager.cs`: Integrate with phase processing

### Game Integration
- `Domain/Aggregates/Game.cs`: Update `AdvanceTurn()` to auto-process phases

### New Files (Optional)
- `Game/IPhaseExecutor.cs`: Interface for phase execution
- `Game/PhaseExecutors/*.cs`: Executor implementations

## Migration Path

1. **Step 1**: Add phase execution methods
2. **Step 2**: Implement auto-advance logic
3. **Step 3**: Update phases to execute logic
4. **Step 4**: Integrate with turn system
5. **Step 5**: Update console app to use auto-processing
6. **Step 6**: Test and verify

## Future Considerations

- **Stack/Priority**: Main phases will wait for player actions
- **Combat**: Combat phases will wait for player decisions
- **Triggers**: Phases may wait for triggered abilities to resolve
- **State-Based Actions**: Phases may pause for state checks

For now, keep it simple: phases execute and auto-advance. Structure should support waiting in future.

## Notes

- This is a critical fix for proper turn/phase behavior
- Should maintain backward compatibility where possible
- Phases should be extensible for future complexity
- Keep execution simple for now, add complexity later
