# Phase 4.75: Comprehensive Unit Test Plan

## Overview

Phase 4.75 focuses on creating a well-architected, comprehensive unit test suite for the Majik game engine. This plan establishes testing best practices, test organization, coverage goals, and implementation strategy for achieving full unit test coverage.

## Goals

1. **Full Unit Test Coverage**: Achieve 100% code coverage for all domain logic
2. **Well-Architected Test Suite**: Follow testing best practices and patterns
3. **Maintainable Tests**: Tests that are easy to read, understand, and maintain
4. **Fast Execution**: Unit tests should run quickly (< 1 second total)
5. **Isolated Tests**: Each test is independent and can run in any order
6. **Clear Test Structure**: Tests mirror source code structure for easy navigation

## Testing Framework and Tools

### Primary Framework
- **xUnit**: Modern, extensible testing framework for .NET
- **FluentAssertions**: Readable assertion library
- **Moq**: Mocking framework for dependencies
- **coverlet.collector**: Code coverage collection
- **ReportGenerator**: Coverage report generation

### Project Structure
```
Majik.Core.Tests/
├── Majik.Core.Tests.csproj
├── ValueObjects/
│   ├── ManaCostTests.cs
│   ├── ManaPoolTests.cs
│   ├── LifeTotalTests.cs
│   ├── CardIdentityTests.cs
│   ├── PriorityStateTests.cs
│   └── ResolutionStateTests.cs
├── Domain/
│   ├── Aggregates/
│   │   └── GameTests.cs
│   ├── ValueObjects/
│   │   ├── PriorityStateTests.cs
│   │   └── ResolutionStateTests.cs
│   └── Exceptions/
│       ├── DomainExceptionTests.cs
│       ├── InvalidGameStateExceptionTests.cs
│       ├── InvalidPlayerActionExceptionTests.cs
│       └── InvalidZoneTransitionExceptionTests.cs
├── Cards/
│   ├── CardTests.cs
│   ├── PermanentTests.cs
│   ├── CreatureTests.cs
│   ├── LandTests.cs
│   ├── InstantTests.cs
│   ├── SorceryTests.cs
│   ├── EnchantmentTests.cs
│   ├── ArtifactTests.cs
│   └── PlaneswalkerTests.cs
├── Players/
│   └── PlayerTests.cs
├── Services/
│   ├── SpellCasterTests.cs
│   ├── AbilityActivatorTests.cs
│   ├── StackResolverTests.cs
│   ├── ZoneServiceTests.cs
│   ├── PlayerServiceTests.cs
│   ├── GameServiceTests.cs
│   └── ManaAbilityActivatorTests.cs
├── Abilities/
│   ├── ActivatedAbilityTests.cs
│   ├── TriggeredAbilityTests.cs
│   ├── ManaAbilityTests.cs
│   ├── StaticAbilityTests.cs
│   ├── ReplacementEffectTests.cs
│   ├── EffectTests.cs
│   ├── TriggerManagerTests.cs
│   ├── StaticAbilityManagerTests.cs
│   └── ReplacementEffectManagerTests.cs
├── Game/
│   ├── PriorityManagerTests.cs
│   ├── TurnManagerTests.cs
│   └── PhaseManagerTests.cs
├── Stack/
│   └── StackTests.cs
├── Spells/
│   └── SpellTests.cs
├── Costs/
│   ├── ManaCostCostTests.cs
│   ├── AdditionalCostTests.cs
│   ├── CostPaymentTests.cs
│   └── CostValidatorTests.cs
├── Targeting/
│   ├── TargetTests.cs
│   ├── TargetSpecificationTests.cs
│   └── TargetValidatorTests.cs
├── Zones/
│   ├── ZoneTests.cs
│   └── ZoneManagerTests.cs
├── StateMachine/
│   ├── StateMachineTests.cs
│   ├── GameStateMachineTests.cs
│   ├── TurnStateMachineTests.cs
│   └── PhaseStateMachineTests.cs
├── Rules/
│   └── StateBasedActionsTests.cs
├── Events/
│   └── EventBusTests.cs
└── Helpers/
    ├── TestDataBuilder.cs
    ├── TestEventBus.cs
    └── TestHelpers.cs
```

## Test Organization Principles

### 1. Mirror Source Structure
- Test files mirror source file structure
- One test class per source class
- Test class name: `{ClassName}Tests`
- Namespace: `Majik.Core.Tests.{Namespace}`

### 2. Test Class Structure
```csharp
namespace Majik.Core.Tests.ValueObjects;

public class ManaCostTests
{
    // Arrange helpers
    // Test methods organized by feature/behavior
    // Each test follows AAA pattern (Arrange, Act, Assert)
}
```

### 3. Test Method Naming
Follow the pattern: `{MethodName}_{Scenario}_{ExpectedResult}`

Examples:
- `Parse_ValidManaCostString_ReturnsCorrectManaCost()`
- `Pay_InsufficientMana_ReturnsFailure()`
- `CastSpell_InvalidTiming_ThrowsException()`
- `AddManaToPool_ValidMana_UpdatesPool()`

## Testing Strategies by Component Type

### Value Objects

**Characteristics**: Immutable, no dependencies, pure logic

**Testing Approach**:
- Test all factory methods
- Test equality and hash code
- Test validation (invalid inputs)
- Test edge cases
- Test string conversion
- Test mathematical operations

**Example Test Structure**:
```csharp
public class ManaCostTests
{
    [Fact]
    public void Parse_ValidString_ReturnsCorrectManaCost()
    {
        // Arrange
        var input = "3RR";
        
        // Act
        var result = ManaCost.Parse(input);
        
        // Assert
        result.Generic.Should().Be(3);
        result.Red.Should().Be(2);
    }
    
    [Theory]
    [InlineData("3RR", 3, 0, 0, 0, 2, 0)]
    [InlineData("1WU", 1, 1, 1, 0, 0, 0)]
    public void Parse_VariousInputs_ReturnsExpectedValues(string input, int generic, int white, int blue, int black, int red, int green)
    {
        // Test multiple scenarios
    }
    
    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Test equality
    }
    
    [Fact]
    public void Parse_InvalidString_ThrowsException()
    {
        // Test validation
    }
}
```

### Entities

**Characteristics**: Mutable state, business logic, invariants

**Testing Approach**:
- Test constructor validation
- Test business logic methods
- Test invariant protection
- Test state transitions
- Test edge cases
- Mock dependencies where needed

**Example Test Structure**:
```csharp
public class PlayerTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesPlayer()
    {
        // Arrange & Act
        var player = new Player("Alice", 20);
        
        // Assert
        player.Name.Should().Be("Alice");
        player.LifeTotal.Should().Be(20);
        player.HasLost.Should().BeFalse();
    }
    
    [Fact]
    public void LoseLife_ValidAmount_DecreasesLifeTotal()
    {
        // Arrange
        var player = new Player("Alice", 20);
        
        // Act
        player.LoseLife(5);
        
        // Assert
        player.LifeTotal.Should().Be(15);
    }
    
    [Fact]
    public void LoseLife_ReducesToZero_SetsHasLost()
    {
        // Test invariant
    }
    
    [Fact]
    public void AddManaToPool_ValidMana_UpdatesPool()
    {
        // Test mana pool
    }
}
```

### Domain Services

**Characteristics**: Complex operations, dependencies, coordination

**Testing Approach**:
- Mock all dependencies (IEventBus, Stack, etc.)
- Test happy paths
- Test error cases
- Test edge cases
- Test validation
- Verify event publishing
- Test state changes

**Example Test Structure**:
```csharp
public class SpellCasterTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Stack _stack;
    private readonly SpellCaster _spellCaster;
    
    public SpellCasterTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _stack = new Stack(_eventBusMock.Object);
        _spellCaster = new SpellCaster(_stack, _eventBusMock.Object);
    }
    
    [Fact]
    public void CastSpell_ValidSpell_AddsToStack()
    {
        // Arrange
        var card = new Instant("Lightning Bolt", "R");
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));
        
        // Act
        _spellCaster.CastSpell(card, player);
        
        // Assert
        _stack.Count.Should().Be(1);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<SpellCastEvent>()), Times.Once);
    }
    
    [Fact]
    public void CastSpell_InsufficientMana_ThrowsException()
    {
        // Test error case
    }
    
    [Fact]
    public void CastSpell_InvalidTiming_ThrowsException()
    {
        // Test validation
    }
}
```

### Aggregate Roots

**Characteristics**: Complex state, multiple dependencies, invariants

**Testing Approach**:
- Test aggregate invariants
- Test state transitions
- Test coordination of services
- Mock external dependencies
- Test event publishing
- Test validation

**Example Test Structure**:
```csharp
public class GameTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Game _game;
    
    [Fact]
    public void AddPlayer_ValidPlayer_AddsToGame()
    {
        // Test adding players
    }
    
    [Fact]
    public void StartGame_LessThanTwoPlayers_ThrowsException()
    {
        // Test invariant
    }
    
    [Fact]
    public void StartGame_ValidGame_TransitionsToPlaying()
    {
        // Test state transition
    }
}
```

### Managers

**Characteristics**: State management, coordination, business rules

**Testing Approach**:
- Test state management
- Test business rules
- Test edge cases
- Mock dependencies
- Test event publishing

### Abilities

**Characteristics**: Behavior, effects, triggers

**Testing Approach**:
- Test ability creation
- Test activation conditions
- Test effect execution
- Test trigger conditions
- Test resolution

## Test Coverage Goals

### Coverage Targets

| Component Type | Coverage Target | Priority |
|---------------|----------------|----------|
| Value Objects | 100% | Critical |
| Entities | 100% | Critical |
| Domain Services | 100% | Critical |
| Aggregate Roots | 100% | Critical |
| Managers | 95%+ | High |
| Abilities | 95%+ | High |
| State Machines | 90%+ | Medium |
| Events | 85%+ | Medium |
| Helpers/Utilities | 80%+ | Low |

### Overall Coverage Goal
- **Minimum**: 90% line coverage
- **Target**: 95% line coverage
- **Ideal**: 100% line coverage for domain logic

## Test Categories

### Unit Tests (Fast, Isolated)
- Test individual components in isolation
- Mock all dependencies
- Fast execution (< 10ms per test)
- No external dependencies

### Integration Tests (Slower, Real Dependencies)
- Test component interactions
- Use real implementations where appropriate
- Test full workflows
- Separate test project: `Majik.Core.IntegrationTests`

## Test Data Builders

Create test data builders for complex objects:

```csharp
public class PlayerBuilder
{
    private string _name = "Test Player";
    private int _lifeTotal = 20;
    private ManaPool _manaPool = ManaPool.Empty;
    
    public PlayerBuilder WithName(string name)
    {
        _name = name;
        return this;
    }
    
    public PlayerBuilder WithLifeTotal(int life)
    {
        _lifeTotal = life;
        return this;
    }
    
    public Player Build()
    {
        var player = new Player(_name, _lifeTotal);
        // Set mana pool if needed
        return player;
    }
}

// Usage:
var player = new PlayerBuilder()
    .WithName("Alice")
    .WithLifeTotal(20)
    .Build();
```

## Mocking Strategy

### When to Mock
- **Mock**: External dependencies (IEventBus, IZoneService, etc.)
- **Mock**: Complex dependencies that are tested separately
- **Don't Mock**: Value objects (test directly)
- **Don't Mock**: Simple data structures

### Mock Setup Patterns
```csharp
// Setup mock to capture events
var capturedEvents = new List<GameEvent>();
_eventBusMock
    .Setup(x => x.Publish(It.IsAny<GameEvent>()))
    .Callback<GameEvent>(evt => capturedEvents.Add(evt));

// Verify mock calls
_eventBusMock.Verify(x => x.Publish(It.IsAny<SpellCastEvent>()), Times.Once);
```

## Test Execution Strategy

### Test Organization
- Group related tests using `[Fact]` and `[Theory]`
- Use `[Theory]` with `[InlineData]` for parameterized tests
- Use test categories for filtering (if needed)

### Test Execution Order
- Tests should be independent
- No test should depend on another
- Use `[Fact]` for single scenarios
- Use `[Theory]` for multiple scenarios

## Code Coverage Tools

### Setup
```xml
<ItemGroup>
  <PackageReference Include="coverlet.collector" Version="6.0.0">
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <PackageReference Include="ReportGenerator" Version="5.2.0" />
</ItemGroup>
```

### Coverage Reports
- Generate HTML reports
- Track coverage over time
- Set coverage thresholds in CI/CD

## Implementation Plan

### Phase 1: Foundation (Week 1)
1. Create test project structure
2. Set up testing framework (xUnit, FluentAssertions, Moq)
3. Create test helpers and builders
4. Implement value object tests (highest priority)

### Phase 2: Core Domain (Week 2)
1. Entity tests (Card, Player, Permanent, etc.)
2. Aggregate root tests (Game)
3. Domain exception tests

### Phase 3: Services (Week 3)
1. Domain service tests (SpellCaster, AbilityActivator, etc.)
2. Manager tests (PriorityManager, TurnManager, PhaseManager)
3. Stack and resolver tests

### Phase 4: Abilities and Effects (Week 4)
1. Ability tests (Activated, Triggered, Static, Replacement)
2. Effect tests
3. Ability manager tests

### Phase 5: Supporting Systems (Week 5)
1. Cost system tests
2. Targeting tests
3. Zone tests
4. State machine tests

### Phase 6: Rules and Events (Week 6)
1. State-based actions tests
2. Event bus tests
3. Integration tests (separate project)

### Phase 7: Coverage and Polish (Week 7)
1. Achieve coverage goals
2. Review and refactor tests
3. Add missing edge cases
4. Documentation

## Test Quality Standards

### Code Quality
- Tests should be as clean as production code
- Follow SOLID principles
- Use meaningful names
- Keep tests focused (one assertion per test when possible)
- Avoid test code duplication (use helpers/builders)

### Maintainability
- Tests should be easy to understand
- Clear test names that describe behavior
- Good comments for complex scenarios
- Consistent structure across test files

### Performance
- Unit tests should run in < 1 second total
- No I/O operations in unit tests
- No network calls
- Fast feedback loop

## Best Practices

### 1. AAA Pattern
Always use Arrange-Act-Assert:
```csharp
[Fact]
public void TestMethod()
{
    // Arrange
    var player = new Player("Alice", 20);
    
    // Act
    player.LoseLife(5);
    
    // Assert
    player.LifeTotal.Should().Be(15);
}
```

### 2. One Assertion Per Test (When Possible)
Prefer multiple focused tests over one test with many assertions:
```csharp
// Good
[Fact] public void LoseLife_DecreasesLifeTotal() { }
[Fact] public void LoseLife_ZeroLife_SetsHasLost() { }

// Avoid
[Fact] public void LoseLife_DoesEverything() { /* many assertions */ }
```

### 3. Test Behavior, Not Implementation
Test what the code does, not how it does it:
```csharp
// Good: Tests behavior
[Fact]
public void CastSpell_AddsSpellToStack()
{
    // Test that spell is added to stack
}

// Avoid: Tests implementation details
[Fact]
public void CastSpell_CallsStackPush()
{
    // Don't test internal method calls
}
```

### 4. Use Descriptive Test Names
Test names should clearly describe what is being tested:
```csharp
// Good
[Fact]
public void PayMana_InsufficientMana_ReturnsFalse()

// Avoid
[Fact]
public void TestPayMana()
```

### 5. Test Edge Cases
Don't just test happy paths:
- Null inputs
- Empty collections
- Boundary values (0, -1, max values)
- Invalid states
- Error conditions

### 6. Use Test Data Builders
For complex objects, use builders:
```csharp
var spell = new SpellBuilder()
    .WithCard(lightningBolt)
    .WithController(alice)
    .WithTargets(targets)
    .Build();
```

### 7. Mock External Dependencies
Always mock IEventBus, external services, etc.:
```csharp
var eventBusMock = new Mock<IEventBus>();
var service = new MyService(eventBusMock.Object);
```

## Test Documentation

### Test Class Documentation
Each test class should have:
- XML comments describing what is being tested
- Overview of test scenarios
- Notes on any special setup

### Test Method Documentation
Complex tests should have:
- Comments explaining the scenario
- Notes on why certain mocks are set up
- References to rules being tested (Magic rules)

## Continuous Integration

### CI/CD Integration
- Run tests on every commit
- Fail build if tests fail
- Generate coverage reports
- Track coverage trends
- Set coverage thresholds

### Coverage Thresholds
- Fail build if coverage drops below 90%
- Warn if coverage drops below 95%
- Track coverage per component

## Success Criteria

### Functional Requirements
- ✅ All value objects have 100% coverage
- ✅ All entities have 100% coverage
- ✅ All domain services have 100% coverage
- ✅ All aggregate roots have 100% coverage
- ✅ Overall coverage ≥ 95%

### Quality Requirements
- ✅ All tests follow AAA pattern
- ✅ All tests are independent
- ✅ All tests run in < 1 second total
- ✅ Test names clearly describe behavior
- ✅ Tests are maintainable and readable

### Technical Requirements
- ✅ xUnit framework set up
- ✅ FluentAssertions for readable assertions
- ✅ Moq for mocking
- ✅ Coverage tools configured
- ✅ CI/CD integration ready

## Estimated Effort

- **Total Time**: 6-7 weeks
- **Test Files**: ~60-70 test files
- **Test Methods**: ~500-700 test methods
- **Lines of Test Code**: ~15,000-20,000 lines

## Next Steps

1. Create test project structure
2. Set up testing framework and tools
3. Begin with value object tests (highest priority, easiest)
4. Progress through components systematically
5. Continuously measure and improve coverage
6. Refactor tests as needed for maintainability

## Notes

- Start with value objects (easiest, no dependencies)
- Progress to entities (some dependencies)
- Then services (more dependencies, need mocking)
- Finally complex integrations
- Continuously review and refactor tests
- Keep tests as clean as production code
- Tests are documentation - make them readable
