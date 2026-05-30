# PLAN 04 — Monotonic `seq` on state + events, then widen the client reducer

## Goal
Eliminate the bulk of full-snapshot refetches by (1) stamping a per-game monotonic `seq` on every `GameStateDto` and `EventDto` so the portal can drop stale snapshots + detect gaps deterministically (retiring the `reconnectResyncOwnsState` hack), and (2) enriching the `→ Battlefield` `CardMovedEvent` payload + adding a `CounterAddedEvent` payload with the `CardSnapshotDto` fields, so the reducer applies ETB and counter deltas in-place instead of bailing on the two most common live actions.

## Current state (file:line)
- No `seq`. `GameStateDto` (`Majik.Core.Api/Dtos/Dtos.cs:6-13`), `EventDto` (`:55` = `Guid EventId, string Type, DateTime At, JsonElement Payload`). `GameFacade` tracks turn/phase/active (`GameFacade.cs:94-101`) but no event counter; events fan out in `BridgeEvent` (`:798-846`); snapshots in `GetState`/`GetStateFor` (`:698-720`) via `StateSnapshotter.Snapshot` (`:20-42`).
- Reducer-first: `match.ts:451-457` calls `applyEvent`, falling back to `fetchState()` on `false`. `state$` gated by `reconnectResyncOwnsState` (`match.ts:234,319-320,522-527`) with `TODO(reconnect-seq)` at `:516-521`. `game.store.ts:90-93` has a `stateVersion` placeholder for "future server-supplied sequence number."
- Reducer bail-outs: `event.reducer.ts:227` `if (toStr === 'Battlefield') return null;` (refetch on every ETB); `CounterAddedEvent` not in `patchGameState` (`:44-65`) → `default: null` → refetch. `CounterAddedEvent` currently emits an **empty** payload (`EventPayloadBuilder.cs` has no arm → `_ => Empty() :173`).
- `buildCardSnapshot` (`event.reducer.ts:323-341`) hard-codes `power:null, toughness:null, tapped:false, summoningSickness:false` — the gap. Masking centralised: `EventPayloadBuilder.BuildCardMoved :188-214` only emits the rich branch when not masked; a `→ Battlefield` move always touches a public zone → already the revealed branch (enrichment never crosses the masking boundary).

## Design
### Slice 1 — `seq` primitive [S]
`GameFacade._seq` + `NextSeq() => Interlocked.Increment`. Both snapshots and events draw from one counter; a snapshot's seq = the last event folded in. Stamp each event in `BridgeEvent` (set `_lastEventSeq`); `GetState`/`GetStateFor` read `_lastEventSeq` (no increment). Add `long Seq` to `EventDto` + `GameStateDto`; `StateSnapshotter.Snapshot` takes a `seq` param; all event variants of one engine event share the seq (they already share `EventId`). Portal: parse `seq` (`event.types.ts`/`match.types.ts`/`normaliseStateSnapshot`, tolerant of absent → `0`); `game.store.ts` gates — drop `setState` if `next.seq < current.seq`; `applyEvent`: `<= current` → no-op success, `== current+1` → apply, `> current+1` → gap → return `false` (refetch). Delete `reconnectResyncOwnsState`; `state$` → unconditional `setState`.

### Slice 2 — reducer widening [M]
**Battlefield ETB:** enrich the revealed branch of `BuildCardMoved` (`EventPayloadBuilder.cs:204-213`) with `power/toughness/tapped/summoningSickness/abilities/producedManaColors` (shared helper with `StateSnapshotter.SnapshotCard :99`). Portal: drop `event.reducer.ts:227`; extend `buildCardSnapshot` to read the new fields; ETB patches via existing `removeFromZone`/`appendToZone`. **Counters:** add a `CounterAddedEvent` arm to `EventPayloadBuilder.Build` (`{targetInstanceId, counterType, amount, controllerId}`); add `IReadOnlyDictionary<string,int> Counters` to `CardSnapshotDto` + populate in `SnapshotCard`; add a `patchCounterAdded` reducer arm (bump `counters[type]`; display as a badge — do NOT recompute P/T in the reducer; leave authoritative P/T to the snapshot).

## Step-by-step
1–6 (Slice 1, all [S]): `_seq`/`NextSeq`; `Seq` on both DTOs + `StateSnapshotter`; server seq tests; portal parse; store gates; remove `reconnectResyncOwnsState`.
7–10 (Slice 2, [M]): enrich `BuildCardMoved` + `CounterAddedEvent` arm; `Counters` on `CardSnapshotDto` + `SnapshotCard`; portal drop the ETB bail + extend `buildCardSnapshot` + `patchCounterAdded`; `CardSnapshot.counters`.

## Test strategy
Core: `EventPayloadBuilder` enriched-`CardMovedEvent` + `CounterAddedEvent` shape; **masking exactness (CR 706)** — enriched fields only on the revealed branch, masked Hand→Library still `{ownerId,from,to,hidden:true}`; opponent hidden-zone counts unchanged. Seq: scripted game → events `1..N` contiguous, `GetState().Seq` == last. Portal: `event.reducer.spec.ts` ETB round-trip vs a `SnapshotCard` fixture (reducer output == fresh /state), counter application, masked-guard intact; `game.store.spec.ts` stale-drop / duplicate no-op / gap-refetch; a parity test (apply stream then compare to snapshot at same seq → byte-identical for patched zones).

## Risks
Pre-deploy seq absence (portal treats absent as `0`, gate `>`/`<=`; core deploys first); snapshot/event seq race (set `_lastEventSeq` before the subscriber loop; one game = one thread); P/T-from-counters double-count (counters as badge only); gap-detect false positives (per-player variants share the public seq — verified).

## Effort
Slice 1 [S] ~1 day; Slice 2 [M] ~2–3 days. Total ~M.

## Dependencies / sequencing
Slice 1 first (gap-detect covers any reducer miss), then Slice 2. **Api-first deploy** (core before portal; portal tolerant of absent fields). Shares `CardSnapshotDto.Counters` with PLAN 07 — define the shape once.

## Coordination with deferrals agent
Server/wire/client-plumbing only (`EventPayloadBuilder`, `StateSnapshotter`, `Dtos.cs`, portal reducer/store). **No overlap** with card content. Only shared engine surface is read-only consumption of `CounterAddedEvent`/`Permanent.Counters`. The one coordination point is `CardSnapshotDto.Counters`, shared with PLAN 07.

### Critical files
- `Majik.Core.Api/GameFacade.cs`, `StateSnapshotter.cs`, `Dtos/Dtos.cs`, `EventPayloadBuilder.cs`
- `majik.portal/src/app/core/match/event.reducer.ts`, `game.store.ts`, `match.ts`
