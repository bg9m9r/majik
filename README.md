# Majik — engine + server

Open-source Magic: The Gathering rules engine in C# / .NET 10. UI-agnostic, event-driven, state-machine based. Pairs with [`majik.portal`](https://github.com/bg9m9r/majik.portal) (Angular web client) — together they back [majik.tech](https://majik.tech), a free 1v1 client.

Authoritative rules source: [`MagicCompRules 20251114.txt`](./MagicCompRules%2020251114.txt). [`RULES_REFERENCE.md`](./RULES_REFERENCE.md) indexes the most-touched rules.

## Projects

Solution: `Majik.sln`.

| Project | Role |
|---|---|
| `Majik.Core` | Engine library. State machines, stack, priority, SBAs, abilities, effects, zones, cards. Ships the Modern-legal card pool embedded as `CardData/Embedded/modern-cards.json.gz`. |
| `Majik.Core.Api` | DTOs + JSON contracts shared between server and clients. |
| `Majik.Core.SourceGen` | Roslyn source generator — emits the dispatch table that maps `[CardName]` attributes on factory classes to their card names. |
| `Majik.Server` | ASP.NET Core host — REST (`/cards`, `/decks`, `/me`, `/matches`), SignalR `/hubs/match`, OpenAPI at `/openapi/v1.json`. |
| `Majik.Bot` | Game-playing bot (EV search, decision policies). |
| `Majik.Console` | Diagnostic CLI. Two commands: `play-triggers` (triggered-ability playground) and `export-modern-cards` (regenerates the embedded card seed). Not a gameplay UI. |
| `Majik.*.Tests` | xUnit + FluentAssertions + Moq. ~5,975 tests across `Majik.Core.Tests`, `Majik.Core.Api.Tests`, `Majik.Server.Tests`, `Majik.Bot.Tests`. (`Majik.Bot.Tests.Integration` exists but is mostly skipped — bot-vs-bot smoke.) |

## Common commands

```bash
dotnet build Majik.sln
dotnet test  Majik.sln
dotnet test  --filter "FullyQualifiedName~StateBasedActionsTests.LegendRule"

# Server (local dev — needs mongo, see "Local dev" below)
dotnet run --project Majik.Server                   # http://localhost:5057

# Triggered-ability playground
dotnet run --project Majik.Console -- play-triggers [etb|apnap|intervening-if|delayed|all]

# Regenerate the embedded Modern card pool (see "Updating card data")
dotnet run --project Majik.Console -- export-modern-cards <scryfall-all-cards.json>
```

## Local dev

```bash
# 1. Mongo (profiles, decks, matches)
docker compose -f docker-compose.dev.yml up -d

# 2. Server. Card pool is embedded — no seed step required.
dotnet run --project Majik.Server
# -> http://localhost:5057  (REST + /hubs/match + /openapi/v1.json)
```

## Adding a card

The common contributor task. Per-card behaviour lives in a named factory under `Majik.Core/CardData/Factories/`:

```csharp
[CardName("Lightning Bolt")]
public sealed class LightningBoltFactory : NamedCardFactory
{
    public override Card Build(CardEntity entity) => /* ... */;
}
```

`Majik.Core.SourceGen.NamedCardFactoryGenerator` picks up the `[CardName]` attribute at build time and wires the factory into the dispatch table. Many simple spells don't need a hand-written factory at all — the spell-template binders under `Majik.Core/CardData/SpellTemplates/` cover patterns like "deal N damage to any target" via oracle-text matching.

After adding a factory, refresh the embedded seed so `IsImplemented` flips on for the card. See **Updating card data** below.

## Updating card data

The engine's Modern-legal pool ships as `Majik.Core/CardData/Embedded/modern-cards.json.gz` (~22k rows, ~1.8 MB), loaded in-process by `EmbeddedCardRepository` — no database, no HTTP hop. Refresh from a Scryfall bulk export:

```bash
# 1. Fetch Scryfall's "all-cards" bulk (~500 MB JSON).
curl -o /tmp/scryfall.json \
  $(curl -s https://api.scryfall.com/bulk-data \
    | jq -r '.data[] | select(.type=="all_cards") | .download_uri')

# 2. Regenerate the seed. Filters to modern legal/restricted, dedupes by
#    name (highest released_at wins), flags rows with a [CardName] factory
#    as IsImplemented, and round-trips through EmbeddedCardRepository as a
#    sanity check. Non-zero exit means the round-trip failed.
dotnet run --project Majik.Console -- export-modern-cards /tmp/scryfall.json

# 3. Commit.
git add Majik.Core/CardData/Embedded/modern-cards.json.gz
git commit -m "chore(cards): refresh modern-cards seed (Scryfall YYYY-MM-DD)"
```

## Architecture (where things live)

- **Aggregate root:** `Majik.Core.Domain.Aggregates.Game` — owns players, state machines, stack, the managers (`TurnManager`, `PhaseManager`, `PriorityManager`, `StackResolver`, `CombatManager`), `StateBasedActions`, and the `EventBus`. New gameplay surfaces go through `Game`.
- **State machines** (`Majik.Core/StateMachine/` + `Majik.Core/Game/`): `GameStateMachine` (Initializing / Mulligan / Playing / GameOver), `TurnStateMachine`, `PhaseStateMachine`. Pluggable phases support extra-turn / extra-phase / extra-step insertion (Rule 500.7–9).
- **Stack, priority, SBAs:** `Majik.Core/Stack/Stack.cs`, `PriorityManager` + `StackResolver` in `Majik.Core/Services/`, `Majik.Core/Rules/StateBasedActions.cs` (Rule 704), `ActionValidator.cs` + `RulesEngine.cs`.
- **Abilities + effects** (`Majik.Core/Abilities/`, `Majik.Core/Effects/`): `ActivatedAbility`, `TriggeredAbility`, `StaticAbility`, `ReplacementEffect`, `ManaAbility`. `EffectLibrary` + `EffectFactory` produce `IEffect`s. Composed by `SpellCaster`, `AbilityActivator`, `ManaAbilityActivator`.
- **Events:** `IEventBus` / `EventBus` in `Majik.Core/Events/`. Public `GameEvent` subclasses (`CardDrawnEvent`, `PhaseChangedEvent`, …). Domain-internal events under `Majik.Core/Domain/DomainEvents/`.
- **Cards + zones:** `Card` -> `Permanent` -> `Creature` / `Artifact` / `Enchantment` / `Land` / `Planeswalker`; plus `Instant` / `Sorcery`. Zones managed by `ZoneManager` + `ZoneService` so all movement fires events.
- **Card data layer:** `Majik.Core/CardData/EmbeddedCardRepository.cs` (the only `ICardRepository`), `Factories/` (named factories), `SpellTemplates/` (oracle-text binders), `Parsing/` (`KeywordRegistry`, `KeywordParser`).

## Server

`Majik.Server` hosts the REST + SignalR surface the portal consumes.

| Endpoint | Purpose |
|---|---|
| `GET /healthz` | Liveness probe. |
| `GET /openapi/v1.json` | OpenAPI document — portal regenerates its TypeScript client from this. |
| `GET /whoami` | Identity introspection. |
| `GET /me` / `PUT /me` | User profile (handle, etc). |
| `GET /cards/*` | Card catalog (read-only). |
| `GET /decks` / `POST /decks` / `PUT /decks/:id` / `DELETE /decks/:id` / `POST /decks/parse` | Deck CRUD + Arena/MTGO text import. |
| `POST /matches` / `GET /matches/:id` / `POST /matches/:id/seats/:seat/claim` | Match lifecycle. |
| `MapHub /hubs/match` | SignalR — live match state, player actions, prompts. |

Storage:

| Data | Store |
|---|---|
| Cards (Modern-legal pool) | Embedded gzipped JSON in `Majik.Core`. Loaded in-process by `EmbeddedCardRepository`. Refresh via `Majik.Console -- export-modern-cards`. |
| Profiles, decks, matches | MongoDB. |

## Deploy

Render Blueprint in [`render.yaml`](./render.yaml) provisions:

- `majik-api` — Docker web service (this server). Card pool ships inside the image; no persistent disk needed.
- `majik-mongo` — `mongo:7` private service with a persistent disk.

Env var contract + operations live in the umbrella repo:

- [`docs/RENDER_ENV.md`](https://github.com/bg9m9r/majik.project/blob/main/docs/RENDER_ENV.md)
- [`docs/RENDER_MONGO_SETUP.md`](https://github.com/bg9m9r/majik.project/blob/main/docs/RENDER_MONGO_SETUP.md)

## Historical docs

`KEYWORDS_*.md`, `MECHANICS_GAP.md`, `ROADMAP.md`, and similar phase docs live under [`docs/archive/`](./docs/archive/). They are **not authoritative** — code wins when they disagree. Don't update or create new phase docs unless asked.
