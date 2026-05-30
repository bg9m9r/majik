# PLAN 07 — Type the EventDto payloads

## Goal
Replace untyped `JsonElement` event payloads (built as string-keyed anonymous objects server-side, read by string-poking with PascalCase hedges client-side) with per-event payload **records** in `Majik.Core.Api`, exposed via OpenAPI and generated into the portal. The reducer consumes typed shapes; the `?? raw['PascalCase']` hedges go.

## Current state (file:line)
- `EventDto.Payload` is `JsonElement` (`Dtos.cs:55`). Built as anonymous objects in `EventPayloadBuilder.Build` (`:59-174`) — ~16 typed arms + `_ => Empty()` for the rest. There are **47 `GameEvent` subclasses** (`Majik.Core/Events` + `Majik.Core/Domain/DomainEvents`), so ~31 emit `{}`.
- Payload keys are camelCase (`JsonNamingPolicy.CamelCase`, `EventPayloadBuilder.cs:38-41`); the OUTER envelope casing varies → portal hedges.
- Portal reads via `pickString/pickNumber/...` (`event.types.ts:39-71`), each trying `payload[k]` then `payload[Pascal(k)]` (`:41`). The prompt subscriber (`match.ts:465-500`) is full of `raw['x'] ?? raw['X']`.
- Codegen exists: `package.json:10-12` (`openapi:fetch` + `openapi:gen` via `ng-openapi-gen`), config `ng-openapi-gen.json` → `src/app/core/api`.

## Design
Define a closed set of payload records in `Majik.Core.Api/Dtos/EventPayloads.cs`, one per emitted event (`LifeChangedPayload(Guid PlayerId, int Previous, int Current)`, `CardMovedPayload(...)` — masked variant = same record with nulls + `Hidden=true`, matching the dual-shape, etc.).

**Wire strategy (chosen B):** keep `EventDto.Payload` as `JsonElement` on the SignalR wire (SignalR isn't OpenAPI), but expose the payload record types as OpenAPI schemas via a REST schema anchor (e.g. an unused `GET /matches/_eventschemas` returning a record referencing every `*Payload`, or a Swagger schema filter) so `ng-openapi-gen` emits the interfaces into the portal. The reducer casts `evt.payload as LifeChangedPayload` after a `type` discriminator check. SignalR envelope untouched (no runtime risk) + portal gets generated camelCase-correct interfaces.

Portal: once payload keys are guaranteed camelCase by the typed serializer, drop the **inner** `?? Pascal(k)` hedges in `pick*` (keep outer-envelope tolerance in `normaliseEvent`). Convert the prompt subscriber to the generated `PromptDto` model (already a real DTO at `Dtos.cs:107`).

## Step-by-step (incremental over ~47 types)
1. Add `EventPayloads.cs` with records for the 16 currently-emitted types; refactor `EventPayloadBuilder` arms to construct them (behaviour-identical via the same camelCase policy).
2. Add the OpenAPI schema anchor; regenerate; commit generated portal models.
3. Portal: typed `patchGameState` dispatch keyed on `evt.type`, casting per arm; keep `pick*` fallback for un-migrated types.
4. Drop inner PascalCase hedges from `event.types.ts` `pick*` (keep outer tolerance).
5. Convert the prompt subscriber (`match.ts:465-500`) to the generated `PromptDto`.
6. Migrate the remaining ~31 `_ => Empty()` types incrementally (record + builder arm + optional reducer arm each); until migrated they keep emitting `{}` and the reducer refetches — non-breaking.

## Test strategy
Core: golden-JSON tests per record (exact camelCase keys + values); a reflection test asserting every `GameEvent` subclass has a record+arm OR is on an explicit `KnownEmptyPayload` allow-list (prevents silent `{}` regressions, tracks migration). OpenAPI: assert `openapi.json` contains every `*Payload` schema. Portal: reducer specs use generated interfaces; compile-time check the generated shapes match; assert hedge removal doesn't break (fixtures already camelCase).

## Risks
OpenAPI regen + **api-first deploy ordering** (per project memory — core deploys before the portal regenerates+ships; portal already reads prompt fields defensively, so the gap is safe; regen against the new API only). Inner-hedge removal regressions (single shared `CamelCase` policy + golden-JSON tests; remove hedges only for migrated types). Scope creep over 47 types (allow-list test makes it incremental). SignalR vs OpenAPI mismatch (anchor the schemas via REST; `ignoreUnusedModels:true` in `ng-openapi-gen.json:5` means the anchor must actually reference them).

## Effort
M–L. 16-type first cut + codegen plumbing ~M (2–3 days). The ~31-type long tail is ongoing/incremental.

## Dependencies / sequencing
Independent of PLAN 04 but synergistic (typed payloads make PLAN 04's reducer arms cleaner; do PLAN 04 Slice 1 seq first if both in flight). Coordinate `CardSnapshotDto.Counters` (PLAN 04) with the counter payload record here — define once. Within: server records + OpenAPI precede any portal step; prompt typing (step 5) independent after regen.

## Coordination with deferrals agent
Server/wire/codegen only — no factories, no rules. The one interaction: the **OpenAPI regen + api-first deploy ordering**; if their card work adds a new public event (unlikely — card mechanics usually emit existing events), the allow-list test surfaces it rather than silently `{}`. LOW overlap.

### Critical files
- `Majik.Core.Api/EventPayloadBuilder.cs`, `Dtos/Dtos.cs`
- `majik.portal/src/app/core/match/event.types.ts`, `event.reducer.ts`, `match.ts`
