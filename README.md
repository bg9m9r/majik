# Majik — engine + server

Open-source Magic: The Gathering rules engine in C# / .NET 10. UI-agnostic, event-driven, state-machine based. Pairs with [`majik.portal`](https://github.com/bg9m9r/majik.portal) (Angular web client) — together they back [majik.tech](https://majik.tech), a free 1v1 client.

Authoritative rules source: [`MagicCompRules 20251114.txt`](./MagicCompRules%2020251114.txt). [`RULES_REFERENCE.md`](./RULES_REFERENCE.md) indexes the most-touched rules.

## Projects

| Project | Role |
|---|---|
| `Majik.Core` | Engine library — state machines, stack, priority, SBAs, abilities, effects, zones, cards |
| `Majik.Core.Api` | DTOs + JSON contracts shared between server and clients |
| `Majik.Server` | ASP.NET Core host — REST endpoints (`/cards`, `/decks`, `/me`, `/matches`), SignalR `/hubs/match`, OpenAPI at `/openapi/v1.json` |
| `Majik.Console` | Diagnostic CLI: `play-triggers` (triggered-ability playground) + `export-modern-cards` (regenerates the embedded Modern card seed). Not a gameplay UI. |
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

# Engine triggered-ability playground
dotnet run --project Majik.Console -- play-triggers [etb|apnap|intervening-if|delayed|all]

# Regenerate the embedded Modern card pool (see "Updating card data" below)
dotnet run --project Majik.Console -- export-modern-cards <scryfall-all-cards.json>
```

## Updating card data

The engine's Modern-legal card pool ships as a gzipped JSON resource at
`Majik.Core/CardData/Embedded/modern-cards.json.gz` (~22k rows, ~2 MB),
loaded in-process by `EmbeddedCardRepository` — no database, no HTTP hop.
To refresh it from a new Scryfall bulk export:

```bash
# 1. Fetch Scryfall's "all-cards" bulk (~500 MB JSON)
curl -o /tmp/scryfall.json \
  $(curl -s https://api.scryfall.com/bulk-data \
    | jq -r '.data[] | select(.type=="all_cards") | .download_uri')

# 2. Regenerate the seed (filters to modern legal/restricted, dedupes
#    by name preferring the highest released_at, flags rows backed by a
#    [CardName] factory as isImplemented, and round-trips through
#    EmbeddedCardRepository as a sanity check)
dotnet run --project Majik.Console -- export-modern-cards /tmp/scryfall.json

# 3. Commit + push
git add Majik.Core/CardData/Embedded/modern-cards.json.gz
git commit -m "chore(cards): refresh modern-cards seed (Scryfall YYYY-MM-DD)"
```

The CLI exits non-zero if the round-trip verification fails, so a green
exit is a sufficient gate for committing the regenerated file.

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

### Keyword parsing (data side, not gameplay)

`Majik.Core/CardData/Parsing/`:
- `KeywordRegistry` — catalog of known keywords (Official / Parameterized / Custom / CardName).
- `KeywordParser` — lightweight tokenizer used by the binders to recognize keyword lines on Scryfall oracle text.

Per-card behaviour is wired in the named factories under `Majik.Core/CardData/Factories/` (dispatched via `[CardName]` attributes + `Majik.Core.SourceGen.NamedCardFactoryGenerator`) and the spell templates under `Majik.Core/CardData/SpellTemplates/`.

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
| Cards (Modern-legal pool) | Embedded gzipped JSON in `Majik.Core` (`CardData/Embedded/modern-cards.json.gz`, ~22k rows, ~1.8 MB) | Loaded in-process by `EmbeddedCardRepository`. Refresh via `Majik.Console -- export-modern-cards` — see "Updating card data" above. |
| Profiles, decks, matches | MongoDB | Mutable user-scoped data |

### Local dev

```bash
# 1. Mongo (Docker)
docker compose -f docker-compose.dev.yml up -d

# 2. Server (card pool is embedded — no seed step required)
dotnet run --project Majik.Server
# → http://localhost:5057 (REST + /hubs/match + /openapi/v1.json)
```

Auth defaults to disabled when `Auth:Authority` is unset, so the server starts with no IdP configured. To exercise the auth-gated paths locally, point at a Descope project via `appsettings.Development.json` or env vars.

## Testing

xUnit + FluentAssertions + Moq. Tests mirror source layout under `Majik.Core.Tests/`. Shared fixtures: `TestDataBuilder` and `TestEventBus` — use these instead of hand-rolling setup. Rules-engine tests live under `Majik.Core.Tests/Rules/`.

## Deploy

Render Blueprint in [`render.yaml`](./render.yaml) provisions:
- `majik-api` — Docker web service (this server). Card pool ships inside the image via the embedded resource — no persistent disk for cards.
- `majik-mongo` — `mongo:7` private service with persistent disk

Bootstrap procedure, env var contract, and operations in the umbrella repo's `docs/`:
- [`RENDER_ENV.md`](https://github.com/bg9m9r/majik.project/blob/main/docs/RENDER_ENV.md)
- [`RENDER_MONGO_SETUP.md`](https://github.com/bg9m9r/majik.project/blob/main/docs/RENDER_MONGO_SETUP.md)

## Historical docs

`KEYWORDS_*.md`, `MECHANICS_GAP.md`, `ROADMAP.md`, and similar phase docs live under [`docs/archive/`](./docs/archive/). They document prior implementation work and are **not authoritative** — when they conflict with the code, the code wins. Don't update or create new phase docs unless asked.
