# PLAN 08 — Persist engine event-log / snapshots for replica rehydration

## Goal
Make live game state survive a replica restart/crash by durably persisting the canonical ordered event/command stream (and periodic engine snapshots) so a surviving/restarting replica can rehydrate an in-flight `GameFacade` instead of losing the game. Today live state is in-process only; a crash forces the operational "auto-deploy-off" posture (project memory).

## Current state (file:line)
- In-process only: `GameRegistry` is a `ConcurrentDictionary<Guid, GameFacade>` (`GameRegistry.cs:15`); `ServerGameFactory.Create` (`:42-74`) registers a facade; nothing writes engine state to Mongo. `MatchRepository` (`:70`) stores only `Match` metadata, per-handoff not per-event.
- Ordered event stream captured **in memory only**: `MatchReplayBuffer` (`:18-23` "we do NOT persist… survive restart only if rebuilt by future work"), seq-stamped append-only (`:73-77,136`), per-match cap 20k (`:49`), LRU 200 (`:54`), sealed on `Detach` (`:95-102`, `MatchFacadeBridge.cs:232`); stores `envelope.Public` (`MatchFacadeBridge.cs:390`).
- Snapshot exists, no live caller: `GameFacade.SaveSnapshot()` (`GameFacade.cs:689-691`) → `GameSnapshot(State, Log)` where `Log` is the `ActionLog` of submitted commands (`:75,647`). `GameSnapshot.cs:13` documents deterministic re-execution "lands once GameFacade learns to consume an external log on construction (Phase 29.x)" — **no rehydration constructor today**.
- Ownership/sticky routing exists: `MatchOwnership` (Redis `match:{id}:owner`, 30s TTL `:50`, `TryClaimAsync :66`, heartbeat); on owner crash the TTL frees the key but **no replica can take over** (the facade died with the process). `MatchCommandForwarder` forwards commands to the owner over Redis pub/sub (`cmd:match:{id}`), returns `false` when nobody owns it (`:131-135`).
- The live `GameFacade` is not serializable as an object graph → rehydration must be **command/event replay**, not deserialization. The engine is deterministic given (deck-order seed + ordered commands) and has `ActionLog` + seed-driven `StartFullGameAsync(rng:)` (`:533-537`).

## Design (command-log replay canonical; periodic snapshots as optimization)
**Durable store:** per match, an ordered command log + game seed + deck composition. Recommend **Redis stream** (`XADD engine:log:{matchId}`, AOF durability) for the hot append + **periodic Mongo checkpoint** (`SaveSnapshot` + last applied seq every N commands/turns) so rehydration replays only commands since the last checkpoint. Append idempotent on `(matchId, seq)`.

**Rehydration constructor** (`GameFacade.Rehydrate(createInputs, seed, commandsSinceCheckpoint)`): build via the existing `Create` path, start the driver with the same seed, feed each logged command through `SubmitAsync` to fast-forward, **suppressing event fan-out during replay** (don't attach the bridge until replay completes).

**Rehydration path:** command arrives → `MatchOwnership.GetOwnerAsync` null/expired → `TryClaimAsync` → on success, `GameRegistry.Get` miss → load seed + checkpoint + commands-since, `Rehydrate`, register, `MatchFacadeBridge.Attach`, dispatch. Extend `MatchCommandForwarder.OnClaimedAsync` (`:94`) to trigger rehydration so a forwarded command is served by the new owner.

**Consistency:** only the `TryClaimAsync` (`SETNX :83`) winner may rehydrate (no split-brain). Pin + persist the RNG seed at creation. Align the durable log seq with PLAN 04's `seq` so the client's gap-detect handles the discontinuity (client refetches `/state`; the rehydrated facade serves an authoritative snapshot at the correct seq).

## Step-by-step (sliced)
1. **[S]** Persist match seed + deck composition at creation.
2. **[M]** Durable command log: append `(matchId, seq, LoggedCommand)` on every accepted `SubmitCommandAsync` (Redis stream + Mongo checkpoint, idempotent).
3. **[M]** `GameFacade.Rehydrate(...)` replaying commands with fan-out suppressed; `ServerGameFactory.Rehydrate` wrapper.
4. **[M]** Periodic checkpoint (`SaveSnapshot` + last seq) every N commands/turns.
5. **[L]** Wire claim→rehydrate at the ownership-claim site; rehydrate-on-miss, register, Attach, dispatch.
6. **[M]** Persist the replay buffer to the durable store (closes `:18-23`; doubles as verification stream).
7. **[S]** Operational flag gating rehydration (mirror inert-without-Redis posture); re-enable auto-deploy once proven.

## Test strategy
Determinism: create a game, record the command log, rehydrate from `(seed, log)`, assert `GetState()` byte-identical (across combat/counters/mulligan). Checkpoint+replay: rehydration from `checkpoint@seqK + commands(K+1..N)` == full replay from 0. Idempotency: double-append no-op; double-claim → one facade. Failure injection: start on replica A, drop A's facade + expire ownership, route next command to B, assert B rehydrates with a contiguous seq stream (fake `IConnectionMultiplexer`; `MatchRepository` fault seam `:11`); also a checkpoint-write failure → fall back to full-log replay. Ownership race: two simultaneous claims → exactly one rehydrates.

## Risks (big surface)
- **Determinism is load-bearing.** Card `InstanceId`/event `EventId` use `Guid.NewGuid()` — replay produces different ids, breaking the portal's id-keyed reducer + persisted references. **Central risk.** Spike first: seed instance-id generation deterministically, or persist an id↔slot map in the checkpoint and remap on replay; if impossible, fall back to snapshot-only rehydration (display-faithful but lossy for in-flight stack/priority).
- Replay latency vs `MatchCommandForwarder.ReplyTimeout` 5s (`:49`) → checkpoints bound replay; rehydrate async + return "rehydrating, retry."
- Event double-broadcast → suppress fan-out during `Rehydrate`.
- Split-brain → fence rehydration on a fresh successful claim; rely on the CAS release (`MatchOwnership.cs:139-145`).
- Write amplification → Redis hot path + periodic Mongo checkpoint + capped retention.
- Seq consistency with PLAN 04 → the durable log's seq is authoritative.

## Effort
L–XL. The determinism spike (instance-id stability) + claim→rehydrate wiring + failure-injection harness dominate. Snapshot-only fallback L; full deterministic command-replay XL.

## Dependencies / sequencing
**Depends on PLAN 04 Slice 1 (`seq`)** for a coherent post-rehydration resync. The instance-id determinism spike is a hard prerequisite gate before committing to command-replay. Builds on existing `MatchOwnership`/`MatchCommandForwarder`/`MatchReplayBuffer`/`SaveSnapshot`+`ActionLog` — no new ownership model.

## Coordination with deferrals agent
Infra/persistence/server only — no factories, no rules, no `CardData`. One cross-cutting constraint to flag: **determinism depends on engine reproducibility** — any new mechanic introducing non-determinism (wall-clock, unordered iteration, fresh Guids in resolution) breaks replay-based rehydration. New mechanics must stay deterministic given `(seed, command order)`. Otherwise LOW overlap.

### Critical files
- `Majik.Core.Api/GameFacade.cs`, `GameSnapshot.cs`, `GameRegistry.cs`
- `Majik.Server/Matches/MatchReplayBuffer.cs`, `MatchOwnership.cs`, `MatchCommandForwarder.cs`
