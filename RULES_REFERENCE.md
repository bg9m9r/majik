# Magic: The Gathering Comprehensive Rules Reference

## Overview

This document references the official Magic: The Gathering Comprehensive Rules (2025-11-14) as the authoritative source for all game logic decisions in the Majik engine.

## Rules Document

**File**: `MagicCompRules 20251114.txt`

**Effective Date**: November 14, 2025

**Usage**: This document should be consulted when:
- Implementing game mechanics
- Making decisions about turn/phase structure
- Implementing card abilities
- Handling edge cases
- Resolving game state questions
- Verifying rule compliance

## Critical Rules for Current Implementation

### Turn Structure (Rule 500)

**Rule 500.1**: A turn consists of five phases:
1. **Beginning Phase** (with steps: Untap, Upkeep, Draw)
2. **Pre-Combat Main Phase**
3. **Combat Phase** (with steps: Beginning of Combat, Declare Attackers, Declare Blockers, Combat Damage, End of Combat)
4. **Post-Combat Main Phase**
5. **Ending Phase** (with steps: End, Cleanup)

**Rule 500.2**: A phase or step in which players receive priority ends when the stack is empty and all players pass in succession.

**Rule 500.3**: Steps in which no players receive priority (Untap, certain Cleanup steps) end when all specified actions are completed.

**Rule 500.7**: Extra turns are added directly after the specified turn. Most recently created turn is taken first.

**Rule 500.8**: Extra phases are added directly after the specified phase. Most recently created phase occurs first.

**Rule 500.9**: Extra steps are added directly after or before a specified step. Most recently created step occurs first.

**Rule 500.11**: Steps, phases, or turns can be skipped.

### Starting the Game (Rule 103)

**Rule 103.7**: The starting player skips their draw step during their first turn.

**Note**: This is correctly implemented in our `FirstTurnSequence` which omits the Draw step.

### Priority and Timing (Rule 117)

**Rule 117.3**: The active player receives priority at the beginning of most steps and phases.

**Rule 117.4**: If the stack is empty when a player would receive priority, that player receives priority.

**Rule 117.5**: If the stack is not empty, the top object on the stack resolves, then the active player receives priority.

### State-Based Actions (Rule 704)

**Rule 704.1**: State-based actions are checked whenever a player would receive priority.

**Rule 704.5**: A player loses the game if they have 0 or less life.

**Rule 704.5b**: A player loses the game if they're required to draw a card from an empty library.

## Key Rules Sections for Engine Development

### Turn Structure (Section 5: Turn Structure)
- **500. General**: Turn structure overview
  - **500.1**: Turn consists of 5 phases: Beginning, Pre-Combat Main, Combat, Post-Combat Main, Ending
  - **500.2**: Phases/steps with priority end when stack is empty and all players pass
  - **500.3**: Steps without priority (Untap, certain Cleanup) end when actions complete
  - **500.7**: Extra turns added after specified turn, most recent first
  - **500.8**: Extra phases added after specified phase, most recent first
  - **500.9**: Extra steps added after/before specified step, most recent first
  - **500.11**: Steps, phases, or turns can be skipped
- **501. Beginning Phase**: Contains Untap, Upkeep, Draw steps
- **502. Untap Step**: No priority, untap permanents
- **503. Upkeep Step**: Players receive priority
- **504. Draw Step**: Draw card, then players receive priority
- **505. Main Phase**: Pre-combat and post-combat main phases
- **506. Combat Phase**: Contains combat steps
- **507-511. Combat Steps**: Beginning of Combat, Declare Attackers, Declare Blockers, Combat Damage, End of Combat
- **512. Ending Phase**: Contains End and Cleanup steps
- **513. End Step**: Players receive priority
- **514. Cleanup Step**: Usually no priority, discard to hand size, remove damage

### Priority and the Stack (Section 1: Game Concepts)
- **117. Timing and Priority**: When players can take actions
- **405. Mana Abilities**: Mana generation timing
- **406. Activated Abilities**: When abilities can be activated
- **407. Triggered Abilities**: When triggers go on the stack

### Zones (Section 4: Zones)
- **400. General**: Zone rules
- **401. Library**: Library zone rules
- **402. Hand**: Hand zone rules
- **403. Battlefield**: Battlefield zone rules
- **404. Graveyard**: Graveyard zone rules
- **406. Stack**: Stack zone rules
- **407. Exile**: Exile zone rules

### State-Based Actions (Section 7: Additional Rules)
- **704. State-Based Actions**: When state-based actions are checked
- **704.5**: Player loses the game
- **704.5a**: Player loses at 0 or less life
- **704.5b**: Player loses if required to draw from empty library

### Turn-Based Actions (Section 5)
- **503.1**: Beginning of combat step
- **507. Declare Attackers Step**: Attacker declaration
- **508. Declare Blockers Step**: Blocker declaration
- **510. Combat Damage Step**: Damage assignment

## Implementation Guidelines

### 1. Turn Structure
- Follow exact phase/step order from rules (Rule 500.1)
- Implement all mandatory steps
- Handle optional steps correctly
- Support extra turns and phases (Rules 500.7, 500.8, 500.9)
- Note: Our implementation treats steps as distinct states, which is acceptable for sequencing

### Current Implementation Notes

**Phase/Step Hierarchy**:
- According to rules: 5 phases, each with steps
- Our implementation: Treats each step as a distinct state
- **Rationale**: Simplifies sequencing while maintaining correct order
- **Future**: May want to add phase-level grouping for clarity

**Phase Sequence** (Rule 500.1):
1. Beginning Phase → Untap, Upkeep, Draw steps ✅
2. Pre-Combat Main Phase ✅
3. Combat Phase → Beginning of Combat, Declare Attackers, Declare Blockers, Combat Damage, End of Combat ✅
4. Post-Combat Main Phase ✅
5. Ending Phase → End, Cleanup steps ✅

**First Turn** (Rule 103.7):
- Draw step is skipped ✅ (Correctly implemented)

### 2. Priority System
- Active player gets priority first
- Priority passes after each action
- Stack must be empty to advance phase
- Players can hold priority

### 3. State-Based Actions
- Check after each event
- Execute in order
- Repeat until no actions execute
- Check before priority passes

### 4. Zone Transitions
- Follow zone change rules
- Handle replacement effects
- Trigger zone change events
- Maintain zone visibility rules

## Rules Compliance Checklist

When implementing features, verify:
- [ ] Follows official turn structure
- [ ] Implements all mandatory steps
- [ ] Handles priority correctly
- [ ] Checks state-based actions
- [ ] Follows zone transition rules
- [ ] Implements triggers correctly
- [ ] Handles replacement effects
- [ ] Supports all zone types

## Notes

- Rules are the source of truth
- When in doubt, consult the rules document
- Edge cases should reference specific rule numbers
- Implementation should match rules exactly
- Document any deviations with rationale

## Rule Number References

When implementing features, reference specific rule numbers:
- Example: "Rule 500.1: A turn consists of five phases..."
- Example: "Rule 117.3: The active player receives priority..."
- Example: "Rule 704.5a: A player loses the game if..."

This helps with:
- Documentation
- Testing
- Debugging
- Future reference
