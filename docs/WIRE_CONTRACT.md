# Wire Contract

This document pins the authoritative engine fields that cross the
`Majik.Server` → portal boundary, and the three invariants that the server
clock and the portal seat-identity logic MUST uphold. The rule throughout:
**the engine is authoritative; everything downstream DERIVES from it and
never recomputes it.**

Canonical rules reference: `MagicCompRules 20251114.txt`. Cited rule numbers
below (e.g. CR 117) are load-bearing.

## Authoritative engine fields

These are produced by the engine (`Majik.Core`) and surfaced through
`GameFacade` / `EventPayloadBuilder` / `StateSnapshotter`. The server and
portal read them; they do not invent their own.

| Field | Source | Meaning |
| --- | --- | --- |
| `turnNumber` | `TurnStartedEvent.TurnNumber`, `GameStateDto.TurnNumber` | The current turn (CR 102.1). Monotonic, increments at each turn start. |
| disambiguated **phase / step** | `PhaseLabelResolver.Resolve(phase, turnState)` → `EventDto` payload (`phase` / `step`) and `GameStateDto.Phase` | The CR 505 step name. The raw enum value `Main` is ALWAYS split into `PreCombatMain` / `PostCombatMain` before it reaches the wire. |
| `activePlayerId` | `GameFacade.ActivePlayerId` (tracked from `TurnStartedEvent`; falls back to the priority holder, then Alice) | The id of the player whose turn it is (CR 102.1 / 103.7). |
| per-viewer seat `playerId` | `PromptDto.PlayerId`, `EventDto` payload `playerId`, `GameStateDto` player ids | The engine seat id. Creator → Alice, Opponent → Bob (see `MatchFacadeBridge.PromptRouting` and `ServerGameFactory.Create`). |

### Authoritative phase / step vocabulary

The only labels that may appear on the wire as a phase/step:

```
Untap, Upkeep, Draw, PreCombatMain, BeginningOfCombat, DeclareAttackers,
DeclareBlockers, CombatDamage, EndOfCombat, PostCombatMain, End, Cleanup
```

The raw `Main` label is NOT in this set — it must be disambiguated (CR 505).

## The three invariants

### 1. Clock ↔ priority (CR 117 / 103.7)

The server clock holder MUST equal the engine's active player. The active
player gets priority first each turn (CR 117), and the clock that burns is
the active player's.

- **Derivation:** `MatchService.OnPriorityPassedAsync` decrements the
  previous holder's clock, moves the holder, reschedules the timeout, and
  republishes `match.clock-update`. It is invoked by `MatchFacadeBridge`
  whenever `GameFacade.ActivePlayerId` changes seats (mapped engine player →
  sub via `PromptRouting`).
- **Anti-pattern (the bug this slice fixed):** setting the holder once in
  `PlayDrawAsync` (`firstPlayerSub`) and never updating it — the wrong
  player's clock burned for the whole game. The server clock must DERIVE its
  holder from the engine, never freeze or recompute it.

### 2. Phase vocabulary

Every phase/step string on the wire MUST be in the authoritative vocabulary
above, and the raw `Main` must NEVER appear. Disambiguation happens once, at
the engine→wire seam (`EventPayloadBuilder` via `PhaseLabelResolver`). The
portal renders the label as received; it does not re-map enum values itself.

### 3. Seat identity (Creator → Alice)

A prompt or per-viewer payload addressed to the creator carries
`playerId == Alice.Id`; one for the opponent carries `playerId == Bob.Id`.
The portal DERIVES "is it my turn / is this prompt for me" from the engine's
`playerId` against the viewer's own seat id — it must not recompute seat
identity from sub strings or seating order.

## Enforcement

- `MatchFacadeBridge.AssertAgreement(activePlayerId, clockHolderSeatId,
  wirePhase)` is a dev/test tripwire that throws on a violated clock↔priority
  or phase-vocabulary invariant. It is always present and unit-tested; its
  call sites are gated behind `[Conditional("DEBUG")]` so they compile out in
  Release.
- `Majik.Server.Tests/Matches/LayerAgreementInvariantTests` drives a
  deterministic in-process bot match (`LayerAgreementHarness`) and asserts all
  three invariants end-to-end against captured SignalR publishes.
