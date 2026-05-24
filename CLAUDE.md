# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Majik** — open-source Magic: The Gathering rules engine in C# / .NET 8. UI-agnostic, event-driven, state-machine based. Solution: `Majik.sln` with three projects:

- `Majik.Core/` — engine library
- `Majik.Console/` — CLI for card-data import + keyword analysis (NOT a gameplay UI)
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

# Console tool (Scryfall import + keyword analysis)
dotnet run --project Majik.Console -- import <path-to-scryfall-all-cards.json>
dotnet run --project Majik.Console -- analyze-keywords <path-to-csv>           [--use-claude]
dotnet run --project Majik.Console -- analyze-keywords --from-database         [--use-claude]
dotnet run --project Majik.Console -- ingest-claude-results <path-to-jsonl>
```

`--use-claude` requires `ANTHROPIC_API_KEY` env var or a `.env` file in the repo root (loaded by `DotNetEnv`; the console walks up dirs looking for it). Optional `CLAUDE_MODEL` overrides the default model in `ClaudeKeywordAnalyzer.cs`. See `docs/archive/CLAUDE_SETUP.md` for full setup.

## Card database

SQLite via EF Core (`Microsoft.EntityFrameworkCore.Sqlite`). Path:
`%APPDATA%/Majik/cards.db` (Linux: `~/.config/Majik/cards.db`). DbContext: `Majik.Core/CardData/Database/CardDbContext.cs`. Tables: `Cards`, `CardAbilities`, `EffectReferences`, `CardAbilityEffects`, `KeywordMetadata`, `ClaudeRequestCache`.

Schema is created via `EnsureCreatedAsync()` + ad-hoc `CREATE TABLE IF NOT EXISTS` / `ALTER TABLE` patches in `Program.EnsureKeywordMetadataTableExistsAsync` — no EF migrations in repo. When adding columns to `KeywordMetadata`, mirror the change in both `KeywordMetadataEntity` and that bootstrap method, or older DBs will be missing the column.

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

### Keyword pipeline (data side, not gameplay)

`Majik.Core/CardData/Parsing/`:
1. `OracleTextParser` + `KeywordAbilityParser` + `PatternBasedParser` extract abilities from Scryfall oracle text.
2. `KeywordRegistry` is the catalog of known keywords.
3. `KeywordAnalyzer` categorizes keywords from CSV or DB (Official / Parameterized / Custom / CardName / Unknown).
4. `ClaudeKeywordAnalyzer` calls the Anthropic API (Batches API for bulk) to enrich unknowns + generate implementation notes. Responses are cached in `ClaudeRequestCache` by request-hash to avoid re-billing the same prompt.

`ingest-claude-results` exists because batch results can be downloaded manually; it parses the JSONL, hydrates the cache, and updates `KeywordMetadata` rows.

## Testing

xUnit + FluentAssertions + Moq. Tests mirror source folder layout under `Majik.Core.Tests/`. Shared fixtures: `Majik.Core.Tests/Helpers/TestDataBuilder.cs` and `TestEventBus.cs` — use these for new tests rather than hand-rolling setup. Rules-engine tests live under `Majik.Core.Tests/Rules/`.

## Planning docs

Historical implementation-phase docs (`KEYWORDS_*.md`, `MECHANICS_GAP.md`, `ROADMAP.md`, etc.) live under `docs/archive/`. They document prior decisions but are NOT authoritative — when they conflict with the code, the code wins. Don't update or create new phase docs unless the user explicitly asks.
