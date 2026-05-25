# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Majik** — open-source Magic: The Gathering rules engine in C# / .NET 8. UI-agnostic, event-driven, state-machine based. Solution: `Majik.sln` with three projects:

- `Majik.Core/` — engine library (ships the embedded Modern card pool at `CardData/Embedded/modern-cards.json.gz`)
- `Majik.Console/` — diagnostic CLI: `play-triggers` (triggered-ability playground) + `export-modern-cards` (regenerates the embedded seed from a Scryfall bulk export). Not a gameplay UI.
- `Majik.Core.Tests/` — xUnit + FluentAssertions + Moq

## Common commands

```bash
# Build / restore (run from repo root)
dotnet build Majik.sln
dotnet restore Majik.sln

# Run all tests
dotnet test Majik.sln

# Run tests in one project
dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj

# Run a single test (xUnit filter — full or partial fully-qualified name)
dotnet test --filter "FullyQualifiedName~StateBasedActionsTests.LegendRule"
dotnet test --filter "FullyQualifiedName=Majik.Core.Tests.Rules.LegendRuleTests.LegendRule_AppliesOnlyToCreaturesAndPlaneswalkers"

# Diagnostic console
dotnet run --project Majik.Console -- play-triggers [etb|apnap|intervening-if|delayed|all]
dotnet run --project Majik.Console -- export-modern-cards <path-to-scryfall-all-cards.json>
```

## Card data

The engine reads its Modern-legal card pool from a gzipped JSON resource embedded in `Majik.Core`:

- Resource: `Majik.Core/CardData/Embedded/modern-cards.json.gz` (~22k rows, ~1.8 MB compressed).
- Loader: `Majik.Core/CardData/EmbeddedCardRepository.cs` — the only `ICardRepository` implementation; reads the gzipped JSON into an in-memory `Dictionary<string, CardEntity>` once at startup. No SQLite, no EF Core, no HTTP hop.
- `IsImplemented` is baked into the seed at export time via reflection over `Majik.Core` for `[CardName]`-attributed factories — there is no runtime mutation surface.

To refresh the seed from a new Scryfall bulk export:

```bash
dotnet run --project Majik.Console -- export-modern-cards <path-to-scryfall-all-cards.json>
```

`Majik.Console/Commands/ExportModernCardsCommand.cs` filters to Modern legal/restricted, dedupes by name (highest `released_at` wins), flags `IsImplemented`, writes the gzipped JSON, and round-trips through `EmbeddedCardRepository` as a sanity check. Commit the regenerated `.json.gz` and ship.

## Rules authority

`MagicCompRules 20251114.txt` (the official Comp Rules, 2025-11-14) is the canonical source of truth for game-logic decisions. `RULES_REFERENCE.md` indexes the rules most relevant to current implementation. Cite rule numbers (e.g. `Rule 704.5j`) when implementing or reviewing rules-engine code.

## Architecture

### State machine — hierarchical, three levels

- `GameStateMachine` — top-level: `Initializing | Mulligan | Playing | GameOver` (`Majik.Core/StateMachine/`)
- `TurnStateMachine` — per turn: Beginning / PreCombatMain / Combat / PostCombatMain / Ending
- `PhaseStateMachine` — steps within phases (Untap, Upkeep, Draw, Main, combat steps, End, Cleanup)

Phases are pluggable (`Majik.Core/Game/Phases/`) and the sequence supports MTG's extra-turn / extra-phase / extra-step insertion semantics (Rule 500.7–9). `Majik.Core/Game/PhaseSequence.cs` + `TurnManager` + `PhaseManager` + `PriorityManager` coordinate flow.

### Aggregate root

`Majik.Core.Domain.Aggregates.Game` is the aggregate root. It owns the players, state machines, stack, managers (`TurnManager`, `PhaseManager`, `PriorityManager`, `StackResolver`, `CombatManager`), `StateBasedActions`, and the `EventBus`. New gameplay surfaces should generally be coordinated through `Game` rather than wired directly.

### Event bus

`IEventBus` / `EventBus` in `Majik.Core/Events/`. All gameplay-relevant state changes emit `GameEvent` subclasses (e.g. `CardDrawnEvent`, `PhaseChangedEvent`, `LifeChangedEvent`, `TurnStartedEvent`, `StepStartedEvent`). Domain-internal events live under `Majik.Core/Domain/DomainEvents/` (e.g. `SpellCastEvent`, `PriorityPassedEvent`, `StackObjectResolvedEvent`). Engine is UI-agnostic — any UI/console subscribes via the bus instead of polling.

### Stack + priority + SBAs

- `Majik.Core/Stack/Stack.cs` — the spell/ability stack, holds `IStackObject` items.
- `PriorityManager` + `StackResolver` (`Majik.Core/Services/`) implement priority-passing and resolution loop.
- `Majik.Core/Rules/StateBasedActions.cs` runs SBAs (Rule 704) before any player gets priority.
- `Majik.Core/Rules/ActionValidator.cs` + `RulesEngine.cs` validate player actions.

### Abilities + effects

`Majik.Core/Abilities/` has the four ability shapes: `ActivatedAbility`, `TriggeredAbility` (+ `TriggerManager`), `StaticAbility` (+ `StaticAbilityManager`), `ReplacementEffect` (+ `ReplacementEffectManager`), plus `ManaAbility`. Effects are decoupled: `Majik.Core/Effects/EffectLibrary.cs` + `EffectFactory.cs` produce `IEffect` instances. Costs (`Majik.Core/Costs/`), targeting (`Majik.Core/Targeting/`), and spells (`Majik.Core/Spells/`) are separate subsystems composed by `SpellCaster`, `AbilityActivator`, `ManaAbilityActivator` in `Majik.Core/Services/`.

### Cards + zones

Card hierarchy: `Card` → `Permanent` → `Creature` / `Artifact` / `Enchantment` / `Land` / `Planeswalker`; plus `Instant` / `Sorcery`. Types under `Majik.Core/Cards/Types/`. Zones (`Majik.Core/Zones/`) are managed by `ZoneManager` + `ZoneService`; movement always goes through these so events fire.

### Keyword parsing (data side, not gameplay)

`Majik.Core/CardData/Parsing/`:
- `KeywordRegistry` — catalog of known keywords (Official / Parameterized / Custom / CardName).
- `KeywordParser` — lightweight tokenizer used by the binders to recognize keyword lines on Scryfall oracle text.

Per-card behaviour lives in the named factories under `Majik.Core/CardData/Factories/` (dispatched via `[CardName]` attributes + source generator) and the spell templates under `Majik.Core/CardData/SpellTemplates/`.

## Testing

xUnit + FluentAssertions + Moq. Tests mirror source folder layout under `Majik.Core.Tests/`. Shared fixtures: `Majik.Core.Tests/Helpers/TestDataBuilder.cs` and `TestEventBus.cs` — use these for new tests rather than hand-rolling setup. Rules-engine tests live under `Majik.Core.Tests/Rules/`.

## Planning docs

Historical implementation-phase docs (`KEYWORDS_*.md`, `MECHANICS_GAP.md`, `ROADMAP.md`, etc.) live under `docs/archive/`. They document prior decisions but are NOT authoritative — when they conflict with the code, the code wins. Don't update or create new phase docs unless the user explicitly asks.
