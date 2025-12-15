# Phase 4: Card System and Abilities - Implementation Plan

## Overview

Phase 4 focuses on completing the card system and implementing the full ability system. This phase will enable players to cast spells with costs and targeting, activate abilities, trigger abilities automatically, and apply static abilities. The implementation will follow the official Magic: The Gathering Comprehensive Rules (2025-11-14).

## Goals

1. **Complete Card Type Hierarchy**: Implement all card types (Creature, Land, Instant, Sorcery, Enchantment, Artifact, Planeswalker)
2. **Full Spell Casting**: Implement complete spell casting with costs, targeting, and resolution
3. **Ability System**: Implement triggered, activated, static, and replacement effect abilities
4. **Targeting System**: Enable spells and abilities to target objects and players
5. **Cost System**: Implement mana costs, additional costs, and cost payment
6. **Ability Resolution**: Complete ability resolution with effects
7. **Card Abilities**: Enable cards to have multiple abilities

## Current State Analysis

### What We Have (Phase 1, 1.5, 2, 2.75, 3, 3.5)
- ✅ Basic `Card` class with name, mana cost, owner, controller, zone
- ✅ `ManaCost` value object
- ✅ Basic `Spell` class (on stack)
- ✅ Basic `ActivatedAbility` class (on stack)
- ✅ Stack and priority system
- ✅ Zone system
- ✅ Event system
- ✅ Domain services (SpellCaster, AbilityActivator, StackResolver)
- ✅ DDD structure (value objects, domain events, services)

### What's Missing
- ❌ Card type hierarchy (Creature, Land, Instant, Sorcery, etc.)
- ❌ Card types and subtypes
- ❌ Spell casting with costs
- ❌ Spell casting with targeting
- ❌ Spell resolution (move to battlefield/graveyard)
- ❌ Triggered abilities
- ❌ Static abilities
- ❌ Replacement effects
- ❌ Mana abilities
- ❌ Ability costs
- ❌ Targeting system
- ❌ Ability conditions
- ❌ Ability timing restrictions

## Rules Reference

### Casting Spells (Rule 601)

**Rule 601.1**: To cast a spell is to take it from where it is (usually the hand), put it on the stack, and pay its costs, so that it will eventually resolve and have its effect.

**Rule 601.2**: To cast a spell, a player must:
- Have priority
- Have the card in the appropriate zone
- Follow the steps of casting (601.2a-h)

**Rule 601.2a-h**: Steps of casting:
- a. Announce spell and move to stack
- b. Choose mode (if modal)
- c. Choose targets
- d. Determine total cost
- e. Activate mana abilities
- f. Pay costs
- g. Spell becomes cast
- h. Check for state-based actions

**Rule 601.3**: A player can't begin to cast a spell unless legal targets exist.

**Rule 601.4**: Some spells can't be cast unless certain conditions are met.

### Activating Abilities (Rule 602)

**Rule 602.1**: Activated abilities have a cost and an effect.

**Rule 602.2**: To activate an ability, a player must:
- Have priority
- Have the source in the appropriate zone
- Follow the steps of activation (602.2a-d)

**Rule 602.2a-d**: Steps of activation:
- a. Announce ability and move to stack
- b. Choose mode (if modal)
- c. Choose targets
- d. Determine total cost and pay costs

**Rule 602.3**: A player can't begin to activate an ability unless legal targets exist.

### Triggered Abilities (Rule 603)

**Rule 603.1**: Triggered abilities have a trigger condition and an effect.

**Rule 603.2**: Whenever a trigger condition is met, the ability is put on the stack.

**Rule 603.3**: Triggered abilities can have targets chosen when they trigger.

**Rule 603.4**: Triggered abilities can have intervening-if clauses.

### Static Abilities (Rule 604)

**Rule 604.1**: Static abilities create continuous effects.

**Rule 604.2**: Static abilities don't use the stack.

**Rule 604.3**: Static abilities apply as long as the permanent is on the battlefield.

### Mana Abilities (Rule 605)

**Rule 605.1**: Mana abilities generate mana.

**Rule 605.2**: Mana abilities don't use the stack.

**Rule 605.3**: Mana abilities can be activated during mana payment.

### Targeting (Rule 115)

**Rule 115.1**: Some spells and abilities have targets.

**Rule 115.2**: A target must be chosen for each instance of the word "target" in the text.

**Rule 115.3**: The same target can't be chosen more than once for the same instance of "target".

**Rule 115.4**: A spell or ability can't target itself.

**Rule 115.5**: A spell or ability can't target an object that's not in the appropriate zone.

**Rule 115.7**: Some spells and abilities have restrictions on what they can target.

### Costs (Rule 118)

**Rule 118.1**: A cost is an action a player must take to cast a spell or activate an ability.

**Rule 118.2**: Costs can include:
- Mana costs
- Additional costs
- Alternative costs
- Special actions

**Rule 118.3**: Costs are paid as the spell or ability is cast or activated.

**Rule 118.4**: If a player can't pay a cost, they can't cast the spell or activate the ability.

### Card Types (Rule 3)

**Rule 3.1**: Card types include: Artifact, Creature, Enchantment, Instant, Land, Planeswalker, Sorcery, Tribal.

**Rule 3.2**: Some cards have multiple types.

**Rule 3.3**: Card types determine when and how cards can be played.

## Implementation Plan

### Task 1: Card Type System

**Location**: `Majik.Core/Cards/Types/`

**Purpose**: Create card type hierarchy and type system

**Created Classes**:
- `CardType.cs`: Enumeration of card types (Artifact, Creature, Enchantment, Instant, Land, Planeswalker, Sorcery, Tribal)
- `CardSubtype.cs`: Enumeration of subtypes (optional, for future)
- `CardSupertype.cs`: Enumeration of supertypes (Basic, Legendary, etc.)

**Updated Classes**:
- `ICard.cs`: Add `CardTypes` property (list of types)
- `Card.cs`: Add types property and validation

**Features**:
- Cards can have multiple types
- Type validation
- Type-based casting restrictions

### Task 2: Card Type Hierarchy

**Location**: `Majik.Core/Cards/`

**Created Classes**:

#### 2.1 Permanent Types
- `Permanent.cs`: Base class for permanents (already exists, enhance)
  - Properties: `IsTapped`, `Damage`, `Counters`
  - Methods: `Tap()`, `Untap()`, `TakeDamage()`, `AddCounter()`, `RemoveCounter()`

- `Creature.cs`: Creature permanent
  - Properties: `Power`, `Toughness`, `BasePower`, `BaseToughness`
  - Methods: `GetPower()`, `GetToughness()` (accounting for effects)

- `Land.cs`: Land permanent
  - Methods: `CanTapForMana()`, `TapForMana()`

- `Enchantment.cs`: Enchantment permanent
  - Properties: `EnchantmentType` (Aura, etc.)

- `Artifact.cs`: Artifact permanent
  - Properties: `ArtifactType` (Equipment, etc.)

- `Planeswalker.cs`: Planeswalker permanent
  - Properties: `Loyalty`, `StartingLoyalty`
  - Methods: `AddLoyalty()`, `RemoveLoyalty()`

#### 2.2 Spell Types
- `Spell.cs`: Base spell (already exists, enhance)
  - Add: `CardTypes`, `CanBeCast()`, `GetTargets()`

- `Instant.cs`: Instant spell
  - Can be cast at instant speed

- `Sorcery.cs`: Sorcery spell
  - Can only be cast during main phase with empty stack

**Features**:
- Type-specific properties
- Type-specific behavior
- Type validation

### Task 3: Targeting System

**Location**: `Majik.Core/Targeting/`

**Created Classes**:
- `ITarget.cs`: Interface for targetable objects
- `Target.cs`: Base target implementation
- `TargetSpecification.cs`: Value object specifying what can be targeted
- `TargetValidator.cs`: Service for validating targets

**Target Types**:
- `PlayerTarget`: Target a player
- `CardTarget`: Target a card
- `PermanentTarget`: Target a permanent
- `SpellTarget`: Target a spell on the stack
- `AbilityTarget`: Target an ability on the stack

**Features**:
- Target validation (Rule 115)
- Target legality checking
- Target restrictions
- Multiple targets
- Target requirements

**Integration**:
- `ISpell`: Add `Targets` property
- `IActivatedAbility`: Add `Targets` property
- `ITriggeredAbility`: Add `Targets` property

### Task 4: Cost System

**Location**: `Majik.Core/Costs/`

**Created Classes**:
- `ICost.cs`: Interface for costs
- `ManaCost.cs`: Mana cost (already exists as value object, enhance)
- `AdditionalCost.cs`: Additional costs (sacrifice, tap, etc.)
- `CostPayment.cs`: Service for paying costs
- `CostValidator.cs`: Service for validating costs

**Cost Types**:
- `ManaCost`: Mana payment
- `TapCost`: Tap a permanent
- `SacrificeCost`: Sacrifice a permanent
- `DiscardCost`: Discard a card
- `LifeCost`: Pay life
- `GenericCost`: Generic cost interface for extensibility

**Features**:
- Cost calculation
- Cost payment
- Cost validation
- Alternative costs
- Cost reduction

**Integration**:
- `ISpell`: Add `Cost` property
- `IActivatedAbility`: Add `Cost` property
- `SpellCaster`: Add cost payment logic
- `AbilityActivator`: Add cost payment logic

### Task 5: Triggered Abilities

**Location**: `Majik.Core/Abilities/`

**Created Classes**:
- `ITriggeredAbility.cs`: Interface for triggered abilities
- `TriggeredAbility.cs`: Triggered ability implementation
- `ITrigger.cs`: Interface for triggers
- `Trigger.cs`: Base trigger implementation
- `TriggerManager.cs`: Service for managing triggers

**Trigger Types**:
- `EventTrigger`: Triggers on game events
- `StateTrigger`: Triggers on state changes
- `ZoneChangeTrigger`: Triggers on zone changes
- `DamageTrigger`: Triggers on damage
- `LifeChangeTrigger`: Triggers on life changes

**Features**:
- Trigger conditions
- Trigger timing
- Trigger targets
- Intervening-if clauses
- Multiple triggers per card

**Integration**:
- `ICard`: Add `TriggeredAbilities` property
- `Permanent`: Add triggered abilities
- Event system: Trigger evaluation on events

### Task 6: Static Abilities

**Location**: `Majik.Core/Abilities/`

**Created Classes**:
- `IStaticAbility.cs`: Interface for static abilities
- `StaticAbility.cs`: Static ability implementation
- `StaticAbilityManager.cs`: Service for managing static abilities

**Static Ability Types**:
- `ContinuousEffect`: Continuous effects
- `CharacteristicDefiningAbility`: Defines characteristics
- `ReplacementEffect`: Replacement effects (separate task)

**Features**:
- Continuous effects
- Layer system (future)
- Effect application
- Effect removal

**Integration**:
- `ICard`: Add `StaticAbilities` property
- `Permanent`: Add static abilities
- Game state: Apply static abilities

### Task 7: Replacement Effects

**Location**: `Majik.Core/Abilities/`

**Created Classes**:
- `IReplacementEffect.cs`: Interface for replacement effects
- `ReplacementEffect.cs`: Replacement effect implementation
- `ReplacementEffectManager.cs`: Service for managing replacement effects

**Replacement Effect Types**:
- `PreventDamage`: Prevent damage
- `RedirectDamage`: Redirect damage
- `ReplaceDraw`: Replace card draw
- `ReplaceZoneChange`: Replace zone changes

**Features**:
- Event replacement
- Replacement ordering
- Multiple replacements
- Replacement interaction

**Integration**:
- Event system: Check for replacements before events
- Apply replacements when events occur

### Task 8: Mana Abilities

**Location**: `Majik.Core/Abilities/`

**Created Classes**:
- `IManaAbility.cs`: Interface for mana abilities
- `ManaAbility.cs`: Mana ability implementation
- `ManaPool.cs`: Value object for mana pool
- `ManaAbilityActivator.cs`: Service for activating mana abilities

**Features**:
- Mana generation
- Mana pool management
- Mana payment
- Mana ability timing

**Integration**:
- `Land`: Add mana abilities
- `Permanent`: Add mana abilities
- Cost payment: Use mana pool

### Task 9: Enhanced Spell Casting

**Location**: `Majik.Core/Services/SpellCaster.cs` (update)

**Enhancements**:
- Full casting process (Rule 601.2a-h)
- Target selection
- Cost calculation and payment
- Timing restrictions
- Zone validation
- Spell becomes cast

**Methods**:
- `CanCast(ICard, Player)`: Full validation
- `CastSpell(ICard, Player, IEnumerable<ITarget>, ManaCost)`: Full casting
- `ValidateTargets(ICard, IEnumerable<ITarget>)`: Target validation
- `CalculateCost(ICard, Player)`: Cost calculation
- `PayCosts(ICard, Player, ManaCost)`: Cost payment

**Events**:
- `SpellCastEvent`: Fired when spell is cast
- `TargetsChosenEvent`: Fired when targets are chosen
- `CostsPaidEvent`: Fired when costs are paid

### Task 10: Enhanced Ability Activation

**Location**: `Majik.Core/Services/AbilityActivator.cs` (update)

**Enhancements**:
- Full activation process (Rule 602.2a-d)
- Target selection
- Cost calculation and payment
- Timing restrictions
- Zone validation

**Methods**:
- `CanActivate(IActivatedAbility, Player)`: Full validation
- `ActivateAbility(IActivatedAbility, Player, IEnumerable<ITarget>, ManaCost)`: Full activation
- `ValidateTargets(IActivatedAbility, IEnumerable<ITarget>)`: Target validation
- `CalculateCost(IActivatedAbility, Player)`: Cost calculation
- `PayCosts(IActivatedAbility, Player, ManaCost)`: Cost payment

**Events**:
- `AbilityActivatedEvent`: Fired when ability is activated
- `TargetsChosenEvent`: Fired when targets are chosen
- `CostsPaidEvent`: Fired when costs are paid

### Task 11: Spell Resolution

**Location**: `Majik.Core/Spells/Spell.cs` (update)

**Enhancements**:
- Full resolution process (Rule 608)
- Move to appropriate zone
- Execute spell effects
- Handle permanents vs. non-permanents

**Resolution Logic**:
- Instant/Sorcery: Move to graveyard after resolution
- Permanent: Move to battlefield after resolution
- Execute spell effects
- Check state-based actions

**Integration**:
- `StackResolver`: Handle spell resolution
- `ZoneService`: Move cards to appropriate zones
- Event system: Fire resolution events

### Task 12: Ability Resolution

**Location**: `Majik.Core/Abilities/`

**Enhancements**:
- Full ability resolution (Rule 608)
- Execute ability effects
- Handle ability sources

**Resolution Logic**:
- Execute ability effects
- Handle triggered abilities
- Handle activated abilities
- Check state-based actions

**Integration**:
- `StackResolver`: Handle ability resolution
- Event system: Fire resolution events

### Task 13: State-Based Actions (Foundation)

**Location**: `Majik.Core/Rules/`

**Created Classes**:
- `StateBasedActions.cs`: Service for checking state-based actions
- `ISBA.cs`: Interface for state-based actions

**State-Based Actions**:
- Creature dies (0 or less toughness)
- Planeswalker dies (0 loyalty)
- Player loses (0 or less life)
- Legend rule (future)
- Planeswalker uniqueness rule (future)

**Features**:
- Check after each event
- Check after spell/ability resolution
- Execute state-based actions
- Fire events for state-based actions

**Integration**:
- `Game`: Check state-based actions
- `StackResolver`: Check after resolution
- Event system: Check after events

## Design Decisions

### 1. Card Type System
**Decision**: Use enumeration for types, support multiple types
**Rationale**: 
- Matches Magic rules (cards can have multiple types)
- Type-safe
- Easy to extend

### 2. Targeting System
**Decision**: Separate targeting system with validation
**Rationale**:
- Reusable across spells and abilities
- Clear validation logic
- Follows Magic rules (Rule 115)

### 3. Cost System
**Decision**: Composable cost system with interfaces
**Rationale**:
- Supports various cost types
- Extensible for future costs
- Clear separation of concerns

### 4. Ability System
**Decision**: Separate interfaces for each ability type
**Rationale**:
- Clear separation of concerns
- Type-safe
- Follows Magic rules structure

### 5. Triggered Abilities
**Decision**: Event-driven trigger system
**Rationale**:
- Matches existing event system
- Efficient trigger evaluation
- Easy to extend

### 6. Static Abilities
**Decision**: Continuous effect system
**Rationale**:
- Matches Magic rules (Rule 604)
- Efficient effect application
- Clear effect management

## File Structure

```
Majik.Core/
├── Cards/
│   ├── Types/
│   │   ├── CardType.cs
│   │   ├── CardSubtype.cs
│   │   └── CardSupertype.cs
│   ├── ICard.cs (updated)
│   ├── Card.cs (updated)
│   ├── Permanent.cs (updated)
│   ├── Creature.cs (new)
│   ├── Land.cs (new)
│   ├── Enchantment.cs (new)
│   ├── Artifact.cs (new)
│   ├── Planeswalker.cs (new)
│   ├── Spell.cs (updated)
│   ├── Instant.cs (new)
│   └── Sorcery.cs (new)
├── Targeting/
│   ├── ITarget.cs
│   ├── Target.cs
│   ├── TargetSpecification.cs
│   └── TargetValidator.cs
├── Costs/
│   ├── ICost.cs
│   ├── AdditionalCost.cs
│   ├── CostPayment.cs
│   └── CostValidator.cs
├── Abilities/
│   ├── ITriggeredAbility.cs (new)
│   ├── TriggeredAbility.cs (new)
│   ├── ITrigger.cs (new)
│   ├── Trigger.cs (new)
│   ├── TriggerManager.cs (new)
│   ├── IStaticAbility.cs (new)
│   ├── StaticAbility.cs (new)
│   ├── StaticAbilityManager.cs (new)
│   ├── IReplacementEffect.cs (new)
│   ├── ReplacementEffect.cs (new)
│   ├── ReplacementEffectManager.cs (new)
│   ├── IManaAbility.cs (new)
│   ├── ManaAbility.cs (new)
│   ├── IActivatedAbility.cs (updated)
│   └── ActivatedAbility.cs (updated)
├── Services/
│   ├── SpellCaster.cs (updated)
│   ├── AbilityActivator.cs (updated)
│   ├── ManaAbilityActivator.cs (new)
│   └── StackResolver.cs (updated)
├── Rules/
│   ├── StateBasedActions.cs (new)
│   └── ISBA.cs (new)
└── Domain/
    └── DomainEvents/
        ├── SpellCastEvent.cs (new)
        ├── AbilityActivatedEvent.cs (new)
        ├── TargetsChosenEvent.cs (new)
        ├── CostsPaidEvent.cs (new)
        ├── TriggeredAbilityTriggeredEvent.cs (new)
        └── StateBasedActionExecutedEvent.cs (new)
```

## Rules Compliance

### Casting Spells (Rule 601)
- ✅ Follow casting steps (601.2a-h)
- ✅ Target validation (601.3)
- ✅ Timing restrictions
- ✅ Zone requirements

### Activating Abilities (Rule 602)
- ✅ Follow activation steps (602.2a-d)
- ✅ Target validation (602.3)
- ✅ Timing restrictions
- ✅ Zone requirements

### Triggered Abilities (Rule 603)
- ✅ Trigger on conditions
- ✅ Put on stack automatically
- ✅ Target selection
- ✅ Intervening-if clauses

### Static Abilities (Rule 604)
- ✅ Continuous effects
- ✅ Don't use stack
- ✅ Apply while on battlefield

### Mana Abilities (Rule 605)
- ✅ Generate mana
- ✅ Don't use stack
- ✅ Can activate during mana payment

### Targeting (Rule 115)
- ✅ Target validation
- ✅ Target restrictions
- ✅ Multiple targets
- ✅ Target requirements

### Costs (Rule 118)
- ✅ Cost payment
- ✅ Cost validation
- ✅ Alternative costs
- ✅ Cost reduction

## Success Criteria

### Functional Requirements
- ✅ All card types implemented
- ✅ Full spell casting with costs and targeting
- ✅ Full ability activation with costs and targeting
- ✅ Triggered abilities work automatically
- ✅ Static abilities apply continuously
- ✅ Replacement effects modify events
- ✅ Mana abilities generate mana
- ✅ State-based actions check and execute

### Technical Requirements
- ✅ Code follows DDD patterns
- ✅ All code compiles with 0 errors/warnings
- ✅ Proper encapsulation
- ✅ Value objects where appropriate
- ✅ Domain services for complex operations
- ✅ Domain events for important actions

### Testing Requirements
- ✅ Console app demonstrates spell casting
- ✅ Console app demonstrates ability activation
- ✅ Console app demonstrates triggered abilities
- ✅ Console app demonstrates static abilities
- ✅ All events fire correctly
- ✅ State-based actions work correctly

## Dependencies

### From Previous Phases
- Phase 1: Event system, zones, basic cards
- Phase 1.5: Domain services, value objects
- Phase 2: Turn/phase management
- Phase 2.75: Automatic phase progression
- Phase 3: Stack and priority system
- Phase 3.5: DDD refactoring

### For Phase 5 (Future)
- Combat system will use creatures and abilities
- Damage system will use replacement effects
- Combat abilities will use static abilities

## Implementation Order

1. **Card Type System** (Task 1)
2. **Card Type Hierarchy** (Task 2)
3. **Targeting System** (Task 3)
4. **Cost System** (Task 4)
5. **Enhanced Spell Casting** (Task 9)
6. **Spell Resolution** (Task 11)
7. **Triggered Abilities** (Task 5)
8. **Enhanced Ability Activation** (Task 10)
9. **Ability Resolution** (Task 12)
10. **Static Abilities** (Task 6)
11. **Replacement Effects** (Task 7)
12. **Mana Abilities** (Task 8)
13. **State-Based Actions** (Task 13)

## Risks and Mitigations

### Risk 1: Complexity of Ability System
**Mitigation**: Start with simple abilities, build complexity incrementally

### Risk 2: Targeting Validation Complexity
**Mitigation**: Create comprehensive test cases, reference Magic rules

### Risk 3: Cost System Complexity
**Mitigation**: Start with mana costs, add additional costs incrementally

### Risk 4: Static Ability Layer System
**Mitigation**: Start with simple static abilities, defer layer system to future

## Notes

- This phase builds on Phase 3's stack and priority foundation
- Focus on getting basic functionality working first
- Complex features (layer system, complex replacement effects) can be deferred
- Reference Magic Comprehensive Rules for all decisions
- Maintain DDD principles throughout
