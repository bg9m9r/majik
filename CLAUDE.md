# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## Project

**Majik** — open-source Magic: The Gathering rules engine in C# / .NET 10. UI-agnostic, event-driven, state-machine based.

Solution: `Majik.sln`. Top-level projects:

- `Majik.Core/` — engine library. Ships the Modern card pool embedded at `CardData/Embedded/modern-cards.json.gz`.
- `Majik.Core.Api/` — DTOs + JSON contracts shared with clients.
- `Majik.Core.SourceGen/` — Roslyn source generator that emits the `[CardName]` -> factory dispatch table consumed by `Majik.Core`.
- `Majik.Server/` — ASP.NET Core host. REST + SignalR `/hubs/match` + OpenAPI.
- `Majik.Bot/` — game-playing bot (EV search, decision policies).
- `Majik.Console/` — diagnostic CLI. **Only two subcommands**: `play-triggers` (triggered-ability playground) and `export-modern-cards` (regenerates the embedded card seed). Not a gameplay UI.
- `Majik.*.Tests/` — xUnit + FluentAssertions + Moq. ~5,975 tests across the engine, API, server, and bot suites. `Majik.Bot.Tests.Integration` exists for bot-vs-bot smoke but is mostly skipped — treat it as supplementary.

## Common commands

```bash
dotnet build Majik.sln
dotnet test  Majik.sln
dotnet test  Majik.Core.Tests/Majik.Core.Tests.csproj
dotnet test  --filter "FullyQualifiedName~StateBasedActionsTests.LegendRule"
dotnet test  --filter "FullyQualifiedName=Majik.Core.Tests.Rules.LegendRuleTests.LegendRule_AppliesOnlyToCreaturesAndPlaneswalkers"

dotnet run --project Majik.Console -- play-triggers [etb|apnap|intervening-if|delayed|all]
dotnet run --project Majik.Console -- export-modern-cards <path-to-scryfall-all-cards.json>
```

## Card data

The engine reads its Modern-legal pool from a gzipped JSON resource embedded in `Majik.Core`:

- **Resource:** `Majik.Core/CardData/Embedded/modern-cards.json.gz` (~22k rows, ~1.8 MB compressed).
- **Loader:** `Majik.Core/CardData/EmbeddedCardRepository.cs` — the only `ICardRepository` implementation. Reads the gzipped JSON into an in-memory `Dictionary<string, CardEntity>` once at startup. No database, no HTTP hop.
- **`IsImplemented` flag:** **derived at load time** from the `[CardName]` factory registry (`Majik.Core/CardData/Factories/ImplementedCardNames.cs`) — `EmbeddedCardRepository` overrides whatever the gz stored. The flag stored in the seed is human-inspectable only, never authoritative. No runtime mutation surface.

Because `IsImplemented` is recomputed from code, **a card PR that only adds a `[CardName]` factory does NOT need to regenerate `modern-cards.json.gz`** — the flag flips on automatically. This deliberately ends the binary-seed merge-conflict treadmill (every regen used to conflict with every other open card PR). Only regenerate the seed on a **Scryfall data refresh** (new cards / errata).

To refresh from a Scryfall bulk export:

```bash
dotnet run --project Majik.Console -- export-modern-cards <path-to-scryfall-all-cards.json>
```

`Majik.Console/Commands/ExportModernCardsCommand.cs` filters to Modern legal/restricted, dedupes by name (highest `released_at` wins), stores `IsImplemented` (via the same `ImplementedCardNames` source of truth, for inspection), writes the gzipped JSON, and round-trips through `EmbeddedCardRepository` as a sanity check. Commit the regenerated `.json.gz` (only needed on a data refresh, not per card PR).

## Adding card behaviour

- **Named factory:** create a class under `Majik.Core/CardData/Factories/` with `[CardName("Card Name")]`. `Majik.Core.SourceGen.NamedCardFactoryGenerator` wires it into the dispatch table at build time — no manual registration.
- **Spell template:** for plain-vanilla spells the binders under `Majik.Core/CardData/SpellTemplates/` may already cover the oracle text via regex; check there first before writing a named factory.

Adding a factory flips `IsImplemented` automatically — it is derived from the `[CardName]` registry at load time (`ImplementedCardNames`). **No need to regenerate `modern-cards.json.gz`** for a card PR; that file is only regenerated on a Scryfall data refresh.

## Rules authority

The official Magic: The Gathering Comprehensive Rules (2025-11-14) are the source of truth for game-logic decisions. The text is **not redistributed in this repository** — it is copyrighted by Wizards of the Coast LLC. Download the latest TXT from <https://magic.wizards.com/en/rules> and (optionally) place it at `MagicCompRules 20251114.txt` (gitignored). `RULES_REFERENCE.md` indexes the most-touched rules. Cite rule numbers (e.g. `Rule 704.5j`) when implementing or reviewing rules code.

## Architecture

### State machines (hierarchical, three levels)

- `GameStateMachine` — `Initializing | Mulligan | Playing | GameOver` (`Majik.Core/StateMachine/`).
- `TurnStateMachine` — `Beginning | PreCombatMain | Combat | PostCombatMain | Ending`.
- `PhaseStateMachine` — steps within phases (`Untap`, `Upkeep`, `Draw`, main, combat steps, `End`, `Cleanup`).

Phases are pluggable (`Majik.Core/Game/Phases/`). The sequence supports MTG's extra-turn / extra-phase / extra-step insertion (Rule 500.7–9). Flow is coordinated by `PhaseSequence` + `TurnManager` + `PhaseManager` + `PriorityManager`.

### Aggregate root

`Majik.Core.Domain.Aggregates.Game` owns players, state machines, stack, managers (`TurnManager`, `PhaseManager`, `PriorityManager`, `StackResolver`, `CombatManager`), `StateBasedActions`, and the `EventBus`. New gameplay surfaces are coordinated through `Game`, not wired directly.

### Event bus

`IEventBus` / `EventBus` in `Majik.Core/Events/`. Public `GameEvent` subclasses (`CardDrawnEvent`, `PhaseChangedEvent`, `LifeChangedEvent`, `TurnStartedEvent`, `StepStartedEvent`). Domain-internal events under `Majik.Core/Domain/DomainEvents/` (`SpellCastEvent`, `PriorityPassedEvent`, `StackObjectResolvedEvent`). Engine is UI-agnostic — clients subscribe.

### Stack + priority + SBAs

- `Majik.Core/Stack/Stack.cs` — spell/ability stack holding `IStackObject`s.
- `PriorityManager` + `StackResolver` (`Majik.Core/Services/`) — priority-passing + resolution loop.
- `Majik.Core/Rules/StateBasedActions.cs` — SBAs (Rule 704), run before any player gets priority.
- `Majik.Core/Rules/ActionValidator.cs` + `RulesEngine.cs` — action validation.

### Abilities + effects

`Majik.Core/Abilities/` — the four ability shapes (`ActivatedAbility`, `TriggeredAbility` + `TriggerManager`, `StaticAbility` + `StaticAbilityManager`, `ReplacementEffect` + `ReplacementEffectManager`) plus `ManaAbility`. Effects decoupled via `Majik.Core/Effects/EffectLibrary.cs` + `EffectFactory.cs`. Costs (`Majik.Core/Costs/`), targeting (`Majik.Core/Targeting/`), spells (`Majik.Core/Spells/`) are separate subsystems composed by `SpellCaster`, `AbilityActivator`, `ManaAbilityActivator` in `Majik.Core/Services/`.

### Cards + zones

Card hierarchy: `Card` -> `Permanent` -> `Creature` / `Artifact` / `Enchantment` / `Land` / `Planeswalker`; plus `Instant` / `Sorcery`. Types under `Majik.Core/Cards/Types/`. Zones (`Majik.Core/Zones/`) are managed by `ZoneManager` + `ZoneService`; movement always goes through these so events fire.

### Keyword parsing (data side, not gameplay)

`Majik.Core/CardData/Parsing/`:
- `KeywordRegistry` — catalog of known keywords (Official / Parameterized / Custom / CardName).
- `KeywordParser` — lightweight tokenizer used by the binders to recognize keyword lines on Scryfall oracle text.

Per-card behaviour lives in the named factories under `Majik.Core/CardData/Factories/` (dispatched via `[CardName]` attributes + `Majik.Core.SourceGen`) and the spell templates under `Majik.Core/CardData/SpellTemplates/`.

## Testing

xUnit + FluentAssertions + Moq. Tests mirror source folder layout under `Majik.Core.Tests/`. Shared fixtures: `Majik.Core.Tests/Helpers/TestDataBuilder.cs` + `TestEventBus.cs` — use these instead of hand-rolling setup. Rules-engine tests live under `Majik.Core.Tests/Rules/`.

## Planning docs

Historical phase docs (`KEYWORDS_*.md`, `MECHANICS_GAP.md`, `ROADMAP.md`, etc.) live under `docs/archive/`. They document prior decisions but are **not authoritative** — when they conflict with the code, the code wins. Don't update or create new phase docs unless explicitly asked.
