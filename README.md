# Majik - Magic: The Gathering Game Engine

An open-source, event-driven Magic: The Gathering game engine designed to be UI-agnostic. The engine uses a state machine architecture to handle complex game mechanics including extra turns, extra phases, and dynamic phase insertion.

## Architecture

### State Machine
The engine uses a hierarchical state machine to manage game flow:
- **Game States**: High-level states (Setup, Playing, GameOver)
- **Turn States**: Per-turn states (Beginning, Main, Combat, End)
- **Phase States**: Individual phases within turns (Untap, Upkeep, Draw, etc.)

### Event System
All game actions emit events that can be subscribed to by any UI implementation:
- `GameEvent`: Base event class
- Event types: `CardDrawn`, `PhaseChanged`, `DamageDealt`, `TriggerResolved`, etc.

### Extensibility
The phase system is designed to support:
- Extra turns
- Extra phases (e.g., additional combat phases)
- Dynamic phase insertion based on card abilities
- Priority passing and stack resolution

## Project Structure

```
Majik/
├── Majik.Core/                    # Core engine library
│   ├── StateMachine/              # State machine implementation
│   ├── Events/                    # Event system
│   ├── Game/                      # Game state, turns, phases, priority
│   ├── Zones/                     # Zone management
│   ├── Cards/                     # Card system
│   ├── Abilities/                 # Ability system
│   ├── Players/                   # Player management
│   ├── Combat/                    # Combat system
│   └── Rules/                     # Rules engine
├── Majik.Console/                 # Console testing application
└── README.md
```

## Implementation Plan

See [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) for a comprehensive plan covering:
- Architecture design
- Component breakdown
- Implementation phases
- Testing strategy
- Key design decisions

## Getting Started (Planned API)

```csharp
using Majik.Core;
using Majik.Core.Events;

// Create event bus
var eventBus = new EventBus();

// Subscribe to events
eventBus.Subscribe<CardDrawnEvent>(evt => 
    Console.WriteLine($"Card drawn: {evt.Card.Name}"));

// Create game
var game = new GameState(eventBus);
game.AddPlayer("Player 1");
game.AddPlayer("Player 2");

// Start game
game.StartGame();

// Game runs through state machine
// Events fire for all actions
```

## Design Principles

1. **Event-Driven**: All game actions emit events for UI integration
2. **State Machine**: Hierarchical states manage complex game flow
3. **Extensible**: Phases and turns can be dynamically added
4. **UI-Agnostic**: Engine has no UI dependencies
5. **Composable**: Card abilities compose from base mechanics

## MongoDB (local dev)

New entities (profiles, decks, matches) live in MongoDB. Card data stays on SQLite.

Start the local Mongo container:

```bash
docker compose -f docker-compose.dev.yml up -d
```

Stop:

```bash
docker compose -f docker-compose.dev.yml down
```

Data persists in the `majik-mongo-data` volume. Remove with `docker compose -f docker-compose.dev.yml down -v`.

Production: connection string supplied via `Mongo__ConnectionString` env var (Render).
