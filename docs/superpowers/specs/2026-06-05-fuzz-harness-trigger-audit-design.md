# Bot-vs-Bot Fuzz Harness + Trigger Audits — Design

**Date:** 2026-06-05
**Status:** Approved, pending implementation plan

## Goal

Scale the existing 4-test bot-vs-bot smoke suite into a seeded, repeatable fuzz
harness that catches robustness and silent-correctness bugs across full games.
Primary jobs:

1. **Crash / hang hunting** — many seeded bot games; fail on any exception,
   infinite loop, or game that doesn't terminate under `maxTurns`.
2. **Invariant checking** — assert engine invariants continuously and at game
   end (zone integrity, single result, stack drains, no SBA loop, …).
3. **Missing-trigger detection** — both classes (see below).

Explicit non-goal: a correctness oracle that decides *which* player *should*
have won. Bot games are random walks; they find crashes and invariant breaks,
not proofs of correctness. Per-scenario correctness stays the job of the
existing ~16.8k unit/integration tests.

## Why not "validate every rule + interaction"

The interaction space (≈6k implemented cards × board states) is combinatorial
and unenumerable. A passing fuzz run proves *these paths didn't break*, not
absence of bugs. This harness *complements* the unit suite; it does not replace
it or claim total coverage.

## Components

Three independently testable units.

### 1. `FuzzGameRunner` — `Majik.Bot.Tests.Integration/Fuzz/`

Runs one seeded game: two `BotPlayerAgent`s, a deck pairing, via
`GameFacade.StartFullGameAsync(maxTurns)`. Attaches the invariant observer.
Returns `FuzzResult { seed, deckA, deckB, turns, winner, violations[] }`.

Determinism via the existing `GameRandom.Seed` + `DeterministicIdSource`.

### 2. `GameInvariantObserver`

Subscribes to the `EventBus`. Asserts invariants continuously (per relevant
event) and at game end. Hosts the Class A orphaned-trigger detector (§ Class A).
Wired as a **diagnostic observer injected by the harness** — zero production
cost, engine untouched. (Rejected alternatives: always-on debug assertion =
per-event cost + engine coupling; post-hoc replay analysis = loses live
per-event zone state, worsens false positives.)

### 3. `TriggerWiringAudit` — `Majik.Core.Tests/CardData/`

Class B static test. No game. Enumerates implemented cards, flags
trigger-text-but-no-`ITriggeredAbility`.

## xUnit surface

Per project decision, the fuzzer is an xUnit `[Theory]` in
`Majik.Bot.Tests.Integration` (CI-gated, runs with `dotnet test`).

```csharp
[Theory]
[MemberData(nameof(DeckPairingsBySeed))]
public async Task Fuzz_BotVsBot_NoCrash_NoInvariantViolation(
    string deckA, string deckB, int seed)
```

`DeckPairingsBySeed` = cartesian(existing fixture decks, existing fixture decks)
× seeds `0..N-1`. Decks are the ones `DeckLoader` already loads (Burn, Boros,
…) — known-implemented cards, low flakiness. `N` kept small (e.g. 5) to keep
`dotnet test` wall-clock sane; total cases = pairings × N.

Class B is a separate `[Fact]`/per-card `[Theory]`, no game required.

## Invariants (`GameInvariantObserver`)

| Invariant | When | Note |
|-----------|------|------|
| Termination | game end | finished < maxTurns; silent cap-out flagged as suspicious, not hard fail |
| Single result | game end | exactly one winner OR draw; not both alive + ended |
| Life sanity | `LifeChangedEvent` | no ≤0-life player still active after an SBA pass |
| Zone integrity | `CardMovedEvent` | each card in exactly one zone; counts conserved |
| Stack drains | step/phase end | stack empty when both pass with empty stack; no object crosses turn boundary orphaned |
| No SBA loop | per SBA pass | SBA pass count per checkpoint bounded (catches infinite churn) |
| Priority sanity | priority pass | priority never handed to a player who has lost |
| Class A orphaned trigger | every event | see below |

Hang protection: per-game wall-clock timeout (`CancellationTokenSource`) so an
infinite loop fails as a violation, not a frozen CI job.

## Class A — orphaned-trigger detector (runtime)

The engine **has** the trigger registered but fails to fire it. On each
published `gameEvent`:

1. Enumerate all cards across active zones.
2. For each card's `ITriggeredAbility`, compute
   `expected = Condition.Matches(event, ability) && ability.CanBePutOnStack()
   && source in ActiveZone && !suppressed(Torpor Orb) && (InterveningIf ?? true)`.
3. `actual` = abilities that produced a `TriggeredAbilityTriggeredEvent` for
   this event (subscribe to it).
4. `expected − actual` = orphaned matches → violation (records card, ability,
   event).

Reuses the engine's own predicates (`Condition.Matches`, `CanBePutOnStack`) and
mirrors `TriggerManager.EvaluateTriggers` suppression / intervening-if checks
exactly — so the audit cannot drift from engine semantics.

## Class B — static trigger-wiring audit

The card never had a trigger wired at all (binder regex missed / factory
absent); engine has no internal record one should exist, so it's undetectable at
runtime. Static check per implemented card:

1. Load card (binder chain runs — prod load goes through binders, not the
   test-only named factory).
2. `hasTriggerText` = oracle text matches curated anchored patterns
   (`(^|\n)` + "When" / "Whenever" / "At the beginning of").
3. `hasTriggerAbility` = `card.Abilities.OfType<ITriggeredAbility>().Any()`.
4. `hasTriggerText && !hasTriggerAbility` → flag.

**False-positive control:** an explicit `KnownNonTriggerCards` allowlist
(replacement effects "As ~ enters", "do this only if" static text, reminder-text
only). Each entry carries a one-line reason. The audit fails only on *new*
unexplained gaps and reports the allowlisted count separately. This needs
ongoing curation — accepted cost for catching the silently-inert-card class.

## Failure reproduction

Every violation / crash dumps: seed, deck pair, turn/phase/step, violation type
+ detail, and a `GameSnapshot` (existing save/restore) written to a temp
artifact. Re-run = same seed + snapshot → deterministic repro. The xUnit failure
message leads with the seed and a repro command.

## Testing the harness itself

A green fuzzer that detects nothing is the real risk. Self-tests:

- `GameInvariantObserver`: synthetic event sequences; inject a deliberate
  zone-dup → assert flagged.
- Class A detector: a deliberately-broken stub trigger (registered but fire
  swallowed) → assert flagged; a correctly-firing trigger → assert clean.
- Class B: a known-good card flags nothing; a synthetic
  trigger-text-no-ability card is flagged.

## File map

| Purpose | Location |
|---------|----------|
| Runner + Theory | `Majik.Bot.Tests.Integration/Fuzz/` |
| Invariant observer + Class A | `Majik.Bot.Tests.Integration/Fuzz/` (or shared test lib) |
| Class B static audit | `Majik.Core.Tests/CardData/` |
| Reused: full-game loop | `Majik.Core.Api/GameFacade.cs` (`StartFullGameAsync`) |
| Reused: snapshot repro | `Majik.Core.Api/GameSnapshot.cs` |
| Reused: trigger predicates | `Majik.Core/Abilities/TriggerManager.cs`, `ITriggeredAbility.cs`, `EventTriggerCondition.cs` |
| Reused: fire event | `Majik.Core/Domain/DomainEvents/TriggeredAbilityTriggeredEvent.cs` |
