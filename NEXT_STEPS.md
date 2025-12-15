# Next Steps - Implementation Roadmap

## Current Status

**Completed**: Phases 1, 1.5, 2, 2.5, 2.75, 3, 3.5  
**In Progress**: Phase 4 (85% complete)  
**Remaining**: Phase 4 completion, Phases 5-8

## Immediate Priority: Phase 4 Completion

### Task 1: Mana System (HIGH PRIORITY)
**Why**: Needed for spell casting to work fully. Currently costs are validated but not actually paid from a mana pool.

**Components to Create**:
- `ValueObjects/ManaPool.cs`: Value object tracking mana in pool
- `Abilities/ManaAbility.cs`: Full mana ability implementation  
- `Services/ManaAbilityActivator.cs`: Service for activating mana abilities
- Integration with `CostPayment` to actually deduct mana

**Estimated Time**: 2-3 days

### Task 2: Trigger Manager (HIGH PRIORITY)
**Why**: Triggered abilities exist but don't automatically trigger. Need event-driven system.

**Components to Create**:
- `Abilities/TriggerManager.cs`: Service for managing triggers
- Event subscriptions for trigger evaluation
- Automatic trigger placement on stack
- Trigger condition evaluation

**Estimated Time**: 2-3 days

### Task 3: Static Abilities (MEDIUM PRIORITY)
**Why**: Many cards have static abilities that need to apply continuously.

**Components to Create**:
- `Abilities/IStaticAbility.cs`: Interface (already planned)
- `Abilities/StaticAbility.cs`: Implementation
- `Abilities/StaticAbilityManager.cs`: Service for managing static abilities
- Continuous effect application system

**Estimated Time**: 3-4 days

### Task 4: Replacement Effects (MEDIUM PRIORITY)
**Why**: Needed for damage prevention, event modification, etc.

**Components to Create**:
- `Abilities/IReplacementEffect.cs`: Interface (already planned)
- `Abilities/ReplacementEffect.cs`: Implementation
- `Abilities/ReplacementEffectManager.cs`: Service for managing replacement effects
- Event modification system

**Estimated Time**: 3-4 days

### Task 5: Ability Effects (MEDIUM PRIORITY)
**Why**: Abilities need to actually do something when they resolve.

**Components to Create**:
- `Abilities/IEffect.cs`: Interface for effects
- `Abilities/Effect.cs`: Base effect implementation
- Effect execution system
- Integration with spell/ability resolution

**Estimated Time**: 2-3 days

**Total Estimated Time for Phase 4 Completion**: 12-17 days

## After Phase 4 Completion

### Phase 5: Combat System
**Dependencies**: Phase 4 (creatures, abilities, state-based actions)

**Key Components**:
- CombatManager
- Attacker/Blocker declaration
- Damage calculation
- Combat abilities (first strike, trample, etc.)

**Estimated Time**: 1-2 weeks

### Phase 6: Rules Engine Enhancement
**Dependencies**: Phase 4, Phase 5

**Key Components**:
- Enhanced RulesEngine
- Expanded state-based actions
- Comprehensive rule validation
- Action legality checking

**Estimated Time**: 1-2 weeks

### Phase 7: Advanced Features
**Dependencies**: Phase 4, Phase 5, Phase 6

**Key Components**:
- Complex ability interactions
- Edge case handling
- Multiplayer enhancements

**Estimated Time**: 2-3 weeks

### Phase 8: Testing and Polish
**Dependencies**: All previous phases

**Key Components**:
- Comprehensive test suite
- Performance optimization
- Documentation
- API documentation

**Estimated Time**: 2-3 weeks

## Recommended Implementation Order

1. **Mana System** (enables full spell casting)
2. **Trigger Manager** (enables triggered abilities)
3. **Ability Effects** (makes abilities functional)
4. **Static Abilities** (enables many card effects)
5. **Replacement Effects** (enables damage prevention, etc.)
6. **Combat System** (enables full gameplay)
7. **Rules Engine Enhancement** (ensures correctness)
8. **Advanced Features** (polish and edge cases)
9. **Testing and Polish** (production readiness)

## Success Metrics

### Phase 4 Completion
- ✅ Can cast spells with mana costs
- ✅ Can activate abilities with costs
- ✅ Triggered abilities fire automatically
- ✅ Static abilities apply continuously
- ✅ Replacement effects modify events
- ✅ Abilities execute effects when resolving

### Overall Project
- ✅ Full gameplay loop working
- ✅ Combat system functional
- ✅ Comprehensive rules validation
- ✅ Extensive test coverage
- ✅ Production-ready codebase
