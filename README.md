# Majik — engine + server

Open-source Magic: The Gathering rules engine in C# / .NET 10. UI-agnostic, event-driven, state-machine based. Pairs with [`majik.portal`](https://github.com/bg9m9r/majik.portal) (Angular web client) — together they back [majik.tech](https://majik.tech), a free 1v1 client.

Authoritative rules source: [`MagicCompRules 20251114.txt`](./MagicCompRules%2020251114.txt). [`RULES_REFERENCE.md`](./RULES_REFERENCE.md) indexes the most-touched rules.

## Projects

| Project | Role |
|---|---|
| `Majik.Core` | Engine library — state machines, stack, priority, SBAs, abilities, effects, zones, cards |
| `Majik.Core.Api` | DTOs + JSON contracts shared between server and clients |
| `Majik.Server` | ASP.NET Core host — REST endpoints (`/cards`, `/decks`, `/me`, `/matches`), SignalR `/hubs/match`, OpenAPI at `/openapi/v1.json` |
| `Majik.Console` | CLI for Scryfall import + keyword analysis (not a gameplay UI) |
| `Majik.Tools` | One-off scripts |
| `Majik.*.Tests` | xUnit + FluentAssertions + Moq |

## Common commands

```bash
# Build / restore (run from this directory)
dotnet build Majik.sln
dotnet restore Majik.sln

# Tests
dotnet test Majik.sln
dotnet test --filter "FullyQualifiedName~StateBasedActionsTests.LegendRule"

# Server (local dev)
dotnet run --project Majik.Server                                # http://localhost:5057

# Scryfall import + keyword analysis
dotnet run --project Majik.Console -- import <scryfall-all-cards.json>
dotnet run --project Majik.Console -- analyze-keywords --from-database [--use-claude]
dotnet run --project Majik.Console -- ingest-claude-results <jsonl>
```

`--use-claude` needs `ANTHROPIC_API_KEY` (env var or `.env` walked up from cwd). See [`CLAUDE_SETUP.md`](./CLAUDE_SETUP.md).

## Architecture

### State machines (hierarchical, three levels)

- `GameStateMachine` — `Initializing | Mulligan | Playing | GameOver`
- `TurnStateMachine` — `Beginning | PreCombatMain | Combat | PostCombatMain | Ending`
- `PhaseStateMachine` — steps within phases (`Untap`, `Upkeep`, `Draw`, main, combat steps, `End`, `Cleanup`)

Phases are pluggable (`Majik.Core/Game/Phases/`). The phase sequence supports extra-turn / extra-phase / extra-step insertion per Rule 500.7–9. Flow is coordinated by `PhaseSequence` + `TurnManager` + `PhaseManager` + `PriorityManager`.

### Aggregate root

`Majik.Core.Domain.Aggregates.Game` owns the players, state machines, stack, managers (`TurnManager`, `PhaseManager`, `PriorityManager`, `StackResolver`, `CombatManager`), `StateBasedActions`, and the `EventBus`. New gameplay surfaces go through `Game`, not direct wiring.

### Event bus

`IEventBus` / `EventBus` in `Majik.Core/Events/`. Gameplay state changes emit `GameEvent` subclasses (`CardDrawnEvent`, `PhaseChangedEvent`, `LifeChangedEvent`, etc). Domain-internal events under `Majik.Core/Domain/DomainEvents/` (`SpellCastEvent`, `PriorityPassedEvent`, etc). Engine has no UI dependency — any client subscribes.

### Stack, priority, SBAs

- `Majik.Core/Stack/Stack.cs` — spell/ability stack with `IStackObject` items.
- `PriorityManager` + `StackResolver` — priority-passing + resolution loop.
- `Majik.Core/Rules/StateBasedActions.cs` — SBAs (Rule 704), run before any player gets priority.
- `Majik.Core/Rules/ActionValidator.cs` + `RulesEngine.cs` — action validation.

### Abilities + effects

`Majik.Core/Abilities/` — the four ability shapes: `ActivatedAbility`, `TriggeredAbility` (+ `TriggerManager`), `StaticAbility` (+ `StaticAbilityManager`), `ReplacementEffect` (+ `ReplacementEffectManager`), plus `ManaAbility`. Effects decoupled via `Majik.Core/Effects/EffectLibrary.cs` + `EffectFactory.cs`. Costs (`Majik.Core/Costs/`), targeting (`Majik.Core/Targeting/`), and spells (`Majik.Core/Spells/`) are separate subsystems composed by `SpellCaster`, `AbilityActivator`, `ManaAbilityActivator`.

### Cards + zones

Card hierarchy: `Card` → `Permanent` → `Creature` / `Artifact` / `Enchantment` / `Land` / `Planeswalker`, plus `Instant` / `Sorcery`. Zones (`Majik.Core/Zones/`) are managed by `ZoneManager` + `ZoneService` — all movement goes through these so events fire.

### Keyword pipeline (data side, not gameplay)

`Majik.Core/CardData/Parsing/`:
1. `OracleTextParser` + `KeywordAbilityParser` + `PatternBasedParser` — extract abilities from Scryfall oracle text.
2. `KeywordRegistry` — catalog of known keywords.
3. `KeywordAnalyzer` — categorize keywords (Official / Parameterized / Custom / CardName / Unknown).
4. `ClaudeKeywordAnalyzer` — calls the Anthropic Batches API to enrich unknowns + generate implementation notes. Responses cached in `ClaudeRequestCache` by request-hash.

## Server

`Majik.Server` hosts the REST + SignalR surface the portal consumes.

| Endpoint | Purpose |
|---|---|
| `GET /healthz` | Liveness probe (anonymous) |
| `GET /openapi/v1.json` | OpenAPI document — portal regenerates its TypeScript client from this |
| `GET /whoami` | Identity introspection (requires auth) |
| `GET /me` / `PUT /me` | User profile (handle, etc) |
| `GET /cards/*` | Card catalog (read-only) |
| `GET /decks` / `POST /decks` / `PUT /decks/:id` / `DELETE /decks/:id` / `POST /decks/parse` | Deck CRUD + Arena/MTGO text import |
| `POST /matches` / `GET /matches/:id` / `POST /matches/:id/seats/:seat/claim` | Match lifecycle |
| `MapHub /hubs/match` | SignalR — live match state, player actions, prompts |

### Auth

OIDC bearer tokens, validated against Descope. Server-side:
- `Auth:Authority = https://api.descope.com/<PROJECT_ID>` — JwtBearer discovers keys via `{authority}/.well-known/openid-configuration`.
- `Auth:Audience = <PROJECT_ID>`.
- [`DescopeTokenValidator`](./Majik.Server/Auth/DescopeTokenValidator.cs) accepts both direct and OIDC-app issuer shapes, requires a non-empty `sub`, lifts `discordUserId` from a direct claim or the nested `customAttributes` JSON.
- Disable auth (dev) by leaving `Auth:Authority` empty.

### Storage

| Data | Store | Why |
|---|---|---|
| Cards (Scryfall) | SQLite at `~/.config/Majik/cards.db` (Linux) | Bulk, immutable, ~550 MB. Shipped via GitHub Release in prod — see [`docs/RENDER_CARDS_SEED.md`](../docs/RENDER_CARDS_SEED.md). |
| Profiles, decks, matches | MongoDB | Mutable user-scoped data |

### Local dev

```bash
# 1. Mongo (Docker)
docker compose -f docker-compose.dev.yml up -d

# 2. Cards SQLite — one-time, takes a while
dotnet run --project Majik.Console -- import path/to/scryfall-all-cards.json

# 3. Server
dotnet run --project Majik.Server
# → http://localhost:5057 (REST + /hubs/match + /openapi/v1.json)
```

Auth defaults to disabled when `Auth:Authority` is unset, so the server starts with no IdP configured. To exercise the auth-gated paths locally, point at a Descope project via `appsettings.Development.json` or env vars.

## Testing

xUnit + FluentAssertions + Moq. Tests mirror source layout under `Majik.Core.Tests/`. Shared fixtures: `TestDataBuilder` and `TestEventBus` — use these instead of hand-rolling setup. Rules-engine tests live under `Majik.Core.Tests/Rules/`.

## Card database schema notes

EF Core (`Microsoft.EntityFrameworkCore.Sqlite`). DbContext: `Majik.Core/CardData/Database/CardDbContext.cs`. Tables: `Cards`, `CardAbilities`, `EffectReferences`, `CardAbilityEffects`, `KeywordMetadata`, `ClaudeRequestCache`. Schema created via `EnsureCreatedAsync()` + ad-hoc `CREATE TABLE IF NOT EXISTS` / `ALTER TABLE` patches in `Program.EnsureKeywordMetadataTableExistsAsync` — no EF migrations. When adding columns to `KeywordMetadata`, mirror the change in `KeywordMetadataEntity` AND that bootstrap method, or older DBs will be missing the column.

## Deploy

Render Blueprint in [`render.yaml`](./render.yaml) provisions:
- `majik-api` — Docker web service (this server)
- `majik-mongo` — `mongo:7` private service with persistent disk

Bootstrap procedure, env var contract, and operations in the umbrella repo's `docs/`:
- [`RENDER_ENV.md`](https://github.com/bg9m9r/majik.project/blob/main/docs/RENDER_ENV.md)
- [`RENDER_MONGO_SETUP.md`](https://github.com/bg9m9r/majik.project/blob/main/docs/RENDER_MONGO_SETUP.md)
- [`RENDER_CARDS_SEED.md`](https://github.com/bg9m9r/majik.project/blob/main/docs/RENDER_CARDS_SEED.md)

Cards SQLite is bootstrapped automatically on container boot via [`Majik.Server/docker-entrypoint.sh`](./Majik.Server/docker-entrypoint.sh) — pulls the seed from a GitHub Release when `CARDS_SEED_TAG` changes.

## Historical docs

`PHASE*_PLAN.md` / `PHASE*_COMPLETE.md` / `KEYWORDS_*.md` at the root document prior implementation work. They're useful for context but **not authoritative** — when they conflict with the code, the code wins. Don't update or create new phase docs unless asked.
