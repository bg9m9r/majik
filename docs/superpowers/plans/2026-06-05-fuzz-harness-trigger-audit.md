# Bot-vs-Bot Fuzz Harness + Trigger Audits Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the 4-test bot-vs-bot smoke suite into a seeded fuzz harness that fails on crashes, hangs, engine-invariant violations, and missing triggers (runtime Class A + static Class B).

**Architecture:** A `GameInvariantObserver` subscribes to the engine `EventBus` and asserts invariants per-event + at game end, including a Class A orphaned-trigger detector that recomputes expected trigger fires using the engine's own predicates and reconciles them against `TriggeredAbilityTriggeredEvent`. A `FuzzGameRunner` wires two `BotPlayerAgent`s through `GameFacade.StartFullGameAsync` with the observer attached and a wall-clock timeout, returning a `FuzzResult`. An xUnit `[Theory]` fans out over deck pairings × seeds. A separate `ScryfallCardFactory`-based static audit (Class B) flags implemented cards whose oracle text implies a trigger but which bind no `ITriggeredAbility`.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions. Reuses `GameFacade`, `EventBus`, `TriggerManager` predicates, `GameSnapshot`, `EmbeddedCardRepository`, `ScryfallCardFactory`.

---

## File Structure

**New — `Majik.Bot.Tests.Integration/Fuzz/`:**
- `InvariantViolation.cs` — record describing one violation (kind, detail, turn context).
- `FuzzResult.cs` — record summarizing one game (seed, decks, turns, winner, violations).
- `GameInvariantObserver.cs` — bus subscriber; structural invariants + Class A detector.
- `FuzzGameRunner.cs` — runs one seeded game with observer + timeout; returns `FuzzResult`.
- `BotVsBotFuzzTests.cs` — the `[Theory]` over deck pairings × seeds.
- `GameInvariantObserverTests.cs` — self-tests for the observer (synthetic events).

**New — `Majik.Core.Tests/CardData/`:**
- `TriggerWiringAuditTests.cs` — Class B static audit + its self-tests + allowlist.

**Modified:**
- `Majik.Core.Api/GameFacade.cs` — add `public TriggerManager Triggers => _triggers;` accessor (read-only seam for the observer's ETB-suppression check). Verify the backing field name first.

---

## Task 1: Result + violation data types

**Files:**
- Create: `Majik.Bot.Tests.Integration/Fuzz/InvariantViolation.cs`
- Create: `Majik.Bot.Tests.Integration/Fuzz/FuzzResult.cs`

- [ ] **Step 1: Write `InvariantViolation.cs`**

```csharp
namespace Majik.Bot.Tests.Integration.Fuzz;

/// <summary>One detected breach of an engine invariant during a fuzz game.</summary>
public sealed record InvariantViolation(
    string Kind,        // e.g. "ZoneIntegrity", "SingleResult", "OrphanedTrigger"
    string Detail,      // human-readable specifics, including card/ability names
    int Turn,           // turn number when detected (0 if unknown)
    string Phase);      // phase/step name when detected ("" if unknown)
```

- [ ] **Step 2: Write `FuzzResult.cs`**

```csharp
using System.Collections.Generic;

namespace Majik.Bot.Tests.Integration.Fuzz;

/// <summary>Outcome of a single seeded bot-vs-bot fuzz game.</summary>
public sealed record FuzzResult(
    int Seed,
    string DeckA,
    string DeckB,
    int Turns,
    string? Winner,
    bool TimedOut,
    bool ReachedTurnCap,
    IReadOnlyList<InvariantViolation> Violations);
```

- [ ] **Step 3: Build the project**

Run: `dotnet build Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj`
Expected: build succeeds (records compile; no consumers yet).

- [ ] **Step 4: Commit**

```bash
git add Majik.Bot.Tests.Integration/Fuzz/InvariantViolation.cs Majik.Bot.Tests.Integration/Fuzz/FuzzResult.cs
git commit -s -m "test(fuzz): add FuzzResult + InvariantViolation data types"
```

---

## Task 2: Observer skeleton + zone-integrity invariant (TDD)

The observer subscribes to all events, accumulates violations, and exposes a final-check method. Start with the zone-integrity invariant (each card in exactly one zone).

**Files:**
- Create: `Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserver.cs`
- Test: `Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserverTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Integration.Fuzz;

public class GameInvariantObserverTests
{
    private static (Player alice, Player bob, EventBus bus) NewGame()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        return (alice, bob, bus);
    }

    [Fact]
    public void ZoneIntegrity_SameCardInTwoZones_IsFlagged()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        // Force a corrupt state: one card object present in two zones.
        var card = new Card("Glitch", "");
        card.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(card);
        alice.Zones.Graveyard.AddCard(card);

        observer.RunFinalChecks(turn: 1, phase: "End");

        observer.Violations.Should().Contain(v => v.Kind == "ZoneIntegrity");
    }

    [Fact]
    public void ZoneIntegrity_CleanState_NoViolation()
    {
        var (alice, bob, bus) = NewGame();
        var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

        var card = new Card("Clean", "");
        card.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(card);

        observer.RunFinalChecks(turn: 1, phase: "End");

        observer.Violations.Should().NotContain(v => v.Kind == "ZoneIntegrity");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~GameInvariantObserverTests"`
Expected: FAIL — `GameInvariantObserver` does not exist.

- [ ] **Step 3: Write the minimal observer**

Verify the zone-enumeration API first: `Player.Zones` is a `ZoneManager`; `Zone.GetCards()` returns the cards; battlefield/graveyard/etc. are exposed as properties (`alice.Zones.Battlefield`). Enumerate the standard zones explicitly.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Bot.Tests.Integration.Fuzz;

/// <summary>
/// Subscribes to the engine EventBus and asserts invariants during a fuzz game.
/// Structural checks run on RunFinalChecks(); per-event checks accumulate as events arrive.
/// </summary>
public sealed class GameInvariantObserver : IDisposable
{
    private static readonly ZoneType[] AllZones =
    {
        ZoneType.Battlefield, ZoneType.Graveyard, ZoneType.Hand,
        ZoneType.Exile, ZoneType.Library, ZoneType.Stack, ZoneType.Command,
    };

    private readonly EventBus _bus;
    private readonly IReadOnlyList<Player> _players;
    private readonly Func<int> _etbSuppressionCount;
    private readonly List<InvariantViolation> _violations = new();

    public GameInvariantObserver(EventBus bus, IReadOnlyList<Player> players, Func<int> etbSuppressionCount)
    {
        _bus = bus;
        _players = players;
        _etbSuppressionCount = etbSuppressionCount;
        _bus.SubscribeAll(OnEvent);
    }

    public IReadOnlyList<InvariantViolation> Violations => _violations;

    private void OnEvent(GameEvent e)
    {
        // Per-event checks added in later tasks.
    }

    /// <summary>End-of-game structural invariants.</summary>
    public void RunFinalChecks(int turn, string phase)
    {
        CheckZoneIntegrity(turn, phase);
    }

    private void CheckZoneIntegrity(int turn, string phase)
    {
        var seen = new Dictionary<Guid, string>();
        foreach (var p in _players)
        {
            foreach (var zt in AllZones)
            {
                foreach (var card in EnumerateZone(p, zt))
                {
                    if (seen.TryGetValue(card.InstanceId, out var firstZone))
                    {
                        _violations.Add(new InvariantViolation(
                            "ZoneIntegrity",
                            $"Card '{card.Name}' ({card.InstanceId}) present in both {firstZone} and {zt}.",
                            turn, phase));
                    }
                    else
                    {
                        seen[card.InstanceId] = zt.ToString();
                    }
                }
            }
        }
    }

    private static IEnumerable<ICard> EnumerateZone(Player p, ZoneType zt)
    {
        var zone = p.Zones.GetZone(zt);
        return zone?.GetCards() ?? Enumerable.Empty<ICard>();
    }

    public void Dispose() => _bus.UnsubscribeAll(OnEvent);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~GameInvariantObserverTests"`
Expected: PASS (both tests).

> If `Player.Zones.GetZone(ZoneType)` or `Zone.GetCards()` differ, adjust `EnumerateZone` to the real API confirmed in `Majik.Core/Zones/ZoneManager.cs` + `Zone.cs`. Do not invent members.

- [ ] **Step 5: Commit**

```bash
git add Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserver.cs Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserverTests.cs
git commit -s -m "test(fuzz): GameInvariantObserver with zone-integrity invariant"
```

---

## Task 3: Result + termination invariants (TDD)

Add the end-of-game result invariants: a finished game has exactly one winner or is a draw, and a game that hit the turn cap is flagged (suspicious, not a hard violation). The runner passes the known winner + cap flag into `RunFinalChecks`.

**Files:**
- Modify: `Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserver.cs`
- Test: `Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserverTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Result_BothPlayersAlive_AndNoWinner_NotFlaggedUntilCapKnown()
{
    var (alice, bob, bus) = NewGame();
    var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

    // Natural completion with a winner: clean.
    observer.RunFinalChecks(turn: 5, phase: "End", winnerName: "Alice", reachedTurnCap: false);

    observer.Violations.Should().NotContain(v => v.Kind == "SingleResult");
}

[Fact]
public void Result_NoWinner_NotAtCap_IsFlagged()
{
    var (alice, bob, bus) = NewGame();
    var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

    // Game ended with no winner and we did NOT hit the cap → engine ended a game with no result.
    observer.RunFinalChecks(turn: 5, phase: "End", winnerName: null, reachedTurnCap: false);

    observer.Violations.Should().Contain(v => v.Kind == "SingleResult");
}

[Fact]
public void Result_NoWinner_AtCap_FlaggedSuspiciousNotHard()
{
    var (alice, bob, bus) = NewGame();
    var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

    observer.RunFinalChecks(turn: 30, phase: "End", winnerName: null, reachedTurnCap: true);

    observer.Violations.Should().Contain(v => v.Kind == "TurnCapReached");
    observer.Violations.Should().NotContain(v => v.Kind == "SingleResult");
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~GameInvariantObserverTests"`
Expected: FAIL — `RunFinalChecks` has no `winnerName`/`reachedTurnCap` overload.

- [ ] **Step 3: Update the observer**

Replace the existing `RunFinalChecks` with the richer signature and add the result check:

```csharp
public void RunFinalChecks(int turn, string phase, string? winnerName = null, bool reachedTurnCap = false)
{
    CheckZoneIntegrity(turn, phase);
    CheckResult(turn, phase, winnerName, reachedTurnCap);
}

private void CheckResult(int turn, string phase, string? winnerName, bool reachedTurnCap)
{
    if (reachedTurnCap && winnerName is null)
    {
        _violations.Add(new InvariantViolation(
            "TurnCapReached",
            $"Game reached the turn cap at turn {turn} with no winner (suspicious, not necessarily a bug).",
            turn, phase));
        return;
    }

    if (winnerName is null)
    {
        _violations.Add(new InvariantViolation(
            "SingleResult",
            "Game ended with no winner and the turn cap was not reached.",
            turn, phase));
    }
}
```

Update the two earlier `RunFinalChecks(turn, phase)` call sites in the existing tests to the new signature (they default `winnerName`/`reachedTurnCap`).

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~GameInvariantObserverTests"`
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserver.cs Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserverTests.cs
git commit -s -m "test(fuzz): add result + turn-cap invariants to observer"
```

---

## Task 4: Class A orphaned-trigger detector (TDD)

The detector recomputes, at the moment each game event fires, the set of triggered abilities that **should** fire (using the engine's own `Condition.Matches`, `CanBePutOnStack`, `ActiveZones`, `ActiveWhen`, and the ETB-suppression count), then reconciles against `TriggeredAbilityTriggeredEvent`s (matched by reference equality on the triggering event). At final check, any expected-but-not-fired ability is an `OrphanedTrigger` violation.

**Files:**
- Modify: `Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserver.cs`
- Test: `Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserverTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Majik.Core.Abilities;
using Majik.Core.Domain.DomainEvents;

// A minimal event used to drive matching.
private sealed class PingEvent : GameEvent
{
    public PingEvent() : base(EventType.Custom) { }   // confirm a usable EventType enum member
}

// A trigger condition that matches PingEvent.
private sealed class PingCondition : ITriggerCondition
{
    public bool Matches(GameEvent e, ITriggeredAbility ability) => e is PingEvent;
}

[Fact]
public void ClassA_TriggerThatShouldFireButDoesnt_IsFlagged()
{
    var (alice, bob, bus) = NewGame();
    var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

    // A card on the battlefield whose ability matches PingEvent and is live there.
    var card = new Card("Pinger", "");
    card.SetOwner(alice);
    card.SetZone(ZoneType.Battlefield);
    alice.Zones.Battlefield.AddCard(card);
    var ability = new TriggeredAbility(
        source: card, controller: alice, condition: new PingCondition(),
        activeZones: new[] { ZoneType.Battlefield });
    card.AddAbility(ability);

    // Publish the event but NEVER publish a TriggeredAbilityTriggeredEvent → simulates a swallowed trigger.
    bus.Publish(new PingEvent());

    observer.RunFinalChecks(turn: 1, phase: "Main", winnerName: "Alice");

    observer.Violations.Should().Contain(v => v.Kind == "OrphanedTrigger" && v.Detail.Contains("Pinger"));
}

[Fact]
public void ClassA_TriggerThatFires_IsClean()
{
    var (alice, bob, bus) = NewGame();
    var observer = new GameInvariantObserver(bus, new[] { alice, bob }, () => 0);

    var card = new Card("Pinger", "");
    card.SetOwner(alice);
    card.SetZone(ZoneType.Battlefield);
    alice.Zones.Battlefield.AddCard(card);
    var ability = new TriggeredAbility(
        source: card, controller: alice, condition: new PingCondition(),
        activeZones: new[] { ZoneType.Battlefield });
    card.AddAbility(ability);

    var ping = new PingEvent();
    bus.Publish(ping);
    bus.Publish(new TriggeredAbilityTriggeredEvent(ability, ping)); // engine reported the fire

    observer.RunFinalChecks(turn: 1, phase: "Main", winnerName: "Alice");

    observer.Violations.Should().NotContain(v => v.Kind == "OrphanedTrigger");
}
```

> Confirm the `GameEvent` base ctor + a usable `EventType` value (e.g. `EventType.Custom`). If none fits, reuse an existing concrete event type the binder predicates already match instead of `PingEvent`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~GameInvariantObserverTests"`
Expected: FAIL — no orphaned-trigger logic yet.

- [ ] **Step 3: Implement the detector in the observer**

Add fields + expand `OnEvent`, and reconcile in `RunFinalChecks`. Mirrors `TriggerManager.EvaluateTriggers` matching exactly.

```csharp
// new fields
private readonly List<(GameEvent evt, List<ITriggeredAbility> expected, int turn, string phase)> _expected = new();
private readonly Dictionary<GameEvent, HashSet<ITriggeredAbility>> _fired =
    new(ReferenceEqualityComparer.Instance);
private int _lastTurn;
private string _lastPhase = "";
```

```csharp
private void OnEvent(GameEvent e)
{
    if (e is TriggeredAbilityTriggeredEvent fired)
    {
        if (!_fired.TryGetValue(fired.TriggeringEvent, out var set))
        {
            set = new HashSet<ITriggeredAbility>();
            _fired[fired.TriggeringEvent] = set;
        }
        set.Add(fired.Ability);
        return;
    }

    // Record the abilities that SHOULD fire for this event, evaluated now (zone state is current).
    var expected = new List<ITriggeredAbility>();
    int suppression = _etbSuppressionCount();
    foreach (var ability in EnumerateLiveAbilities())
    {
        if (!ability.Condition.Matches(e, ability)) continue;
        if (!IsSourceInActiveZone(ability)) continue;
        if ((ability.ActiveWhen?.Invoke() ?? true) == false) continue;
        if (!ability.CanBePutOnStack()) continue;                 // covers intervening-if
        if (suppression > 0 && IsCreatureEtbTrigger(e)) continue; // Torpor Orb (CR 603.3)
        expected.Add(ability);
    }

    if (expected.Count > 0)
        _expected.Add((e, expected, _lastTurn, _lastPhase));
}

private IEnumerable<ITriggeredAbility> EnumerateLiveAbilities()
{
    foreach (var p in _players)
        foreach (var zt in AllZones)
            foreach (var card in EnumerateZone(p, zt))
                foreach (var ab in card.Abilities.OfType<ITriggeredAbility>())
                    yield return ab;
}

private static bool IsSourceInActiveZone(ITriggeredAbility ability)
{
    if (ability.Source is not ICard card) return true; // non-card source: don't second-guess
    return ability.ActiveZones.Count == 0 || ability.ActiveZones.Contains(card.Zone);
}

// Best-effort: a creature ETB is a CardMovedEvent into the battlefield of a creature card.
// Use the real CardMovedEvent shape; if uncertain, return false (suppression is rare in fixture decks).
private static bool IsCreatureEtbTrigger(GameEvent e) => false;
```

In `RunFinalChecks`, after the structural checks, reconcile:

```csharp
private void CheckOrphanedTriggers()
{
    foreach (var (evt, expected, turn, phase) in _expected)
    {
        _fired.TryGetValue(evt, out var firedSet);
        foreach (var ability in expected)
        {
            bool didFire = firedSet?.Contains(ability) ?? false;
            if (!didFire)
            {
                var name = ability.Source is ICard c ? c.Name : ability.Source?.ToString() ?? "<unknown>";
                _violations.Add(new InvariantViolation(
                    "OrphanedTrigger",
                    $"Ability on '{name}' matched {evt.GetType().Name} but never fired.",
                    turn, phase));
            }
        }
    }
}
```

Call `CheckOrphanedTriggers()` from `RunFinalChecks`. Track `_lastTurn`/`_lastPhase` in `OnEvent` when a `StepStartedEvent`/`PhaseStateChangedEvent` arrives (confirm event names; if simpler, leave them at the values passed to `RunFinalChecks`).

> `ReferenceEqualityComparer.Instance` is in `System.Collections.Generic` (.NET 5+). Keying `_fired` by reference matches the exact event instance carried on `TriggeredAbilityTriggeredEvent.TriggeringEvent`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~GameInvariantObserverTests"`
Expected: PASS (all observer tests).

- [ ] **Step 5: Commit**

```bash
git add Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserver.cs Majik.Bot.Tests.Integration/Fuzz/GameInvariantObserverTests.cs
git commit -s -m "test(fuzz): Class A orphaned-trigger detector in observer"
```

---

## Task 5: Expose TriggerManager on GameFacade (read-only seam)

The observer's ETB-suppression check needs `TriggerManager.CreatureEtbTriggerSuppressionCount`. Expose the manager read-only.

**Files:**
- Modify: `Majik.Core.Api/GameFacade.cs`

- [ ] **Step 1: Confirm the backing field**

Run: `grep -n "TriggerManager" Majik.Core.Api/GameFacade.cs`
Expected: a private field, e.g. `private readonly TriggerManager _triggers;`. Note its exact name.

- [ ] **Step 2: Add the accessor**

Next to the existing `public IEventBus EventBus => _bus;` accessor, add:

```csharp
/// <summary>Read-only access to the trigger manager (used by fuzz/diagnostic observers).</summary>
public TriggerManager Triggers => _triggers;   // match the real backing-field name from Step 1
```

Add `using Majik.Core.Abilities;` if not already present.

- [ ] **Step 3: Build**

Run: `dotnet build Majik.Core.Api/Majik.Core.Api.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Majik.Core.Api/GameFacade.cs
git commit -s -m "feat(api): expose read-only TriggerManager accessor on GameFacade"
```

---

## Task 6: FuzzGameRunner (TDD)

Runs one seeded game: builds the facade, swaps in two `BotPlayerAgent`s, attaches the observer, runs `StartFullGameAsync` under a timeout, then runs final checks and returns a `FuzzResult`.

**Files:**
- Create: `Majik.Bot.Tests.Integration/Fuzz/FuzzGameRunner.cs`
- Test: `Majik.Bot.Tests.Integration/Fuzz/FuzzGameRunnerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Xunit;

namespace Majik.Bot.Tests.Integration.Fuzz;

public class FuzzGameRunnerTests
{
    [Fact]
    public async Task RunOnce_BurnVsBoros_CompletesWithoutViolations()
    {
        var result = await FuzzGameRunner.RunOnce(
            deckA: "Burn", deckB: "BorosEnergy", seed: 1, maxTurns: 20,
            timeout: TimeSpan.FromSeconds(60));

        result.Violations.Should().BeEmpty(
            because: "a clean bot-vs-bot game must not breach engine invariants:\n"
                + string.Join("\n", result.Violations.Select(v => $"  [{v.Kind}] {v.Detail}")));
        result.TimedOut.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~FuzzGameRunnerTests"`
Expected: FAIL — `FuzzGameRunner` does not exist.

- [ ] **Step 3: Implement the runner**

Mirror the `BotVsBotGameTests` setup exactly (`GameFacade.Create` + `ReplaceAliceAgent`/`ReplaceBobAgent` + `StartFullGameAsync` + `FullGameTask`). Seed both the RNG and the two bot configs from `seed`.

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Majik.Bot;
using Majik.Bot.Tests.Integration.Helpers;
using Majik.Core.Api;
using Majik.Core.Random;

namespace Majik.Bot.Tests.Integration.Fuzz;

public static class FuzzGameRunner
{
    public static async Task<FuzzResult> RunOnce(
        string deckA, string deckB, int seed, int maxTurns, TimeSpan timeout)
    {
        var facade = GameFacade.Create(
            aliceName: $"{deckA}-Bot",
            bobName: $"{deckB}-Bot",
            aliceDeck: DeckLoader.Load(deckA),
            bobDeck: DeckLoader.Load(deckB),
            cardRepo: new Majik.Core.CardData.EmbeddedCardRepository());

        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice, new BotConfig(deckA, RandomSeed: seed * 2 + 1)));
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob, new BotConfig(deckB, RandomSeed: seed * 2 + 2)));

        using var observer = new GameInvariantObserver(
            (Majik.Core.Events.EventBus)facade.EventBus,
            new[] { facade.Alice, facade.Bob },
            () => facade.Triggers.CreatureEtbTriggerSuppressionCount);

        bool timedOut = false;
        bool reachedCap = false;
        string? winner = null;
        int turns = 0;

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await facade.StartFullGameAsync(maxTurns: maxTurns, ct: cts.Token, rng: new GameRandom(seed));
            var gameResult = await facade.FullGameTask!;
            // Read winner/turn count off GameDriver.GameResult — confirm property names.
            winner = gameResult.WinnerName;       // adjust to real property
            turns = gameResult.TurnsPlayed;       // adjust to real property
            reachedCap = turns >= maxTurns && winner is null;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
        }

        observer.RunFinalChecks(turn: turns, phase: "GameEnd", winnerName: winner, reachedTurnCap: reachedCap);

        return new FuzzResult(seed, deckA, deckB, turns, winner, timedOut, reachedCap, observer.Violations.ToList());
    }
}
```

> Confirm: `GameFacade.EventBus` concrete type cast (it may already be `EventBus`); `StartFullGameAsync` accepts an `rng:` arg (it does per the signature); and `GameDriver.GameResult` property names (`WinnerName`/`TurnsPlayed` are guesses — read `Majik.Core/Game/GameDriver.cs` and use the real ones). If the facade doesn't expose `EventBus` as `EventBus`, add a typed accessor or use the `IEventBus` overloads in the observer instead.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~FuzzGameRunnerTests"`
Expected: PASS — one clean game, no violations, not timed out.

- [ ] **Step 5: Commit**

```bash
git add Majik.Bot.Tests.Integration/Fuzz/FuzzGameRunner.cs Majik.Bot.Tests.Integration/Fuzz/FuzzGameRunnerTests.cs
git commit -s -m "test(fuzz): FuzzGameRunner runs one seeded bot-vs-bot game with invariants"
```

---

## Task 7: The fuzz Theory over deck pairings × seeds

**Files:**
- Create: `Majik.Bot.Tests.Integration/Fuzz/BotVsBotFuzzTests.cs`

- [ ] **Step 1: Write the Theory**

```csharp
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Majik.Bot.Tests.Integration.Fuzz;

public class BotVsBotFuzzTests
{
    // The BotDeckCatalog archetypes to fuzz. Extend as the catalog grows.
    private static readonly string[] Archetypes = { "Burn", "BorosEnergy" };
    private const int SeedsPerPairing = 5;
    private const int MaxTurns = 25;

    public static IEnumerable<object[]> DeckPairingsBySeed()
    {
        foreach (var a in Archetypes)
            foreach (var b in Archetypes)
                for (int seed = 0; seed < SeedsPerPairing; seed++)
                    yield return new object[] { a, b, seed };
    }

    [Theory]
    [MemberData(nameof(DeckPairingsBySeed))]
    public async Task Fuzz_BotVsBot_NoCrash_NoInvariantViolation(string deckA, string deckB, int seed)
    {
        var result = await FuzzGameRunner.RunOnce(
            deckA, deckB, seed, MaxTurns, System.TimeSpan.FromSeconds(60));

        result.TimedOut.Should().BeFalse($"seed {seed} {deckA} vs {deckB} hung (possible infinite loop)");
        result.Violations.Should().BeEmpty(
            "no invariant should break. Repro: FuzzGameRunner.RunOnce(\""
            + $"{deckA}\", \"{deckB}\", {seed}, {MaxTurns}, 60s)\n"
            + string.Join("\n", result.Violations.Select(v => $"  [{v.Kind}] T{v.Turn}/{v.Phase}: {v.Detail}")));
    }
}
```

- [ ] **Step 2: Run the Theory**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~BotVsBotFuzzTests"`
Expected: all generated cases PASS. If a case fails, the message carries the repro seed + pairing + violations — investigate as a real engine finding before weakening the test.

- [ ] **Step 3: Commit**

```bash
git add Majik.Bot.Tests.Integration/Fuzz/BotVsBotFuzzTests.cs
git commit -s -m "test(fuzz): bot-vs-bot fuzz Theory over deck pairings x seeds"
```

---

## Task 8: Failure repro — snapshot dump on violation

When a game produces violations (or times out), write a `GameSnapshot` artifact so the failure reproduces deterministically.

**Files:**
- Modify: `Majik.Bot.Tests.Integration/Fuzz/FuzzGameRunner.cs`

- [ ] **Step 1: Confirm the snapshot API**

Run: `grep -n "public .*SaveSnapshot\|public .*Snapshot\|class GameSnapshot" Majik.Core.Api/GameFacade.cs Majik.Core.Api/GameSnapshot.cs`
Expected: a facade method that returns a serializable `GameSnapshot` (e.g. `SaveSnapshot()`), plus a way to serialize it (System.Text.Json, or an existing `ToJson`). Note exact names.

- [ ] **Step 2: Dump on failure**

In `RunOnce`, after `RunFinalChecks`, before returning:

```csharp
if (observer.Violations.Count > 0 || timedOut)
{
    try
    {
        var snapshot = facade.SaveSnapshot();   // adjust to the real method name
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "majik-fuzz");
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, $"fuzz-{deckA}-{deckB}-seed{seed}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        System.IO.File.WriteAllText(path, json);
        System.Console.WriteLine($"[fuzz] repro snapshot written: {path}");
    }
    catch (System.Exception ex)
    {
        System.Console.WriteLine($"[fuzz] snapshot dump failed: {ex.Message}");
    }
}
```

> If `SaveSnapshot()` requires a started/snapshot-capable facade and throws after a crashed game, wrap as above and let the seed alone be the repro (the seed fully determines the run).

- [ ] **Step 3: Run the runner test to confirm no regression**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~FuzzGameRunnerTests"`
Expected: PASS (clean game → no dump written).

- [ ] **Step 4: Commit**

```bash
git add Majik.Bot.Tests.Integration/Fuzz/FuzzGameRunner.cs
git commit -s -m "test(fuzz): dump GameSnapshot artifact on violation/timeout for repro"
```

---

## Task 9: Class B — static trigger-wiring audit (TDD)

A card whose oracle text implies a triggered ability but which binds no `ITriggeredAbility` is silently inert. Build each implemented card through `ScryfallCardFactory` (the faithful prod build) and flag mismatches, minus an explicit allowlist.

**Files:**
- Create: `Majik.Core.Tests/CardData/TriggerWiringAuditTests.cs`

- [ ] **Step 1: Write the self-tests first (synthetic repo)**

```csharp
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class TriggerWiringAuditTests
{
    // Anchored patterns that imply a triggered ability (CR 603.1).
    private static bool HasTriggerText(string? oracle)
    {
        if (string.IsNullOrWhiteSpace(oracle)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(
            oracle,
            @"(^|\n)\s*(When |Whenever |At the beginning of )",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool HasTriggerAbility(Majik.Core.Cards.ICard card) =>
        card.Abilities.OfType<ITriggeredAbility>().Any();

    [Fact]
    public void Synthetic_TriggerText_NoAbility_IsFlagged()
    {
        var alice = new Player("Alice", 20);
        var repo = new TestRepo(new()
        {
            ["Inert Bird"] = new CardEntity
            {
                Name = "Inert Bird", ManaCost = "{1}{W}",
                TypeLine = "Creature — Bird", Power = "2", Toughness = "2",
                // Intentionally a trigger phrasing the binder is unlikely to wire:
                OracleText = "At the beginning of your upkeep, flip a coin and ponder its meaning.",
            },
        });
        var card = new ScryfallCardFactory(repo).Create("Inert Bird", alice);

        (HasTriggerText("At the beginning of your upkeep, flip a coin and ponder its meaning.")
            && !HasTriggerAbility(card)).Should().BeTrue("the audit should flag this card");
    }

    [Fact]
    public void Synthetic_TriggerText_WithAbility_IsClean()
    {
        var alice = new Player("Alice", 20);
        var repo = new TestRepo(new()
        {
            ["Soul Warden"] = new CardEntity
            {
                Name = "Soul Warden", ManaCost = "{W}",
                TypeLine = "Creature — Human Cleric", Power = "1", Toughness = "1",
                OracleText = "Whenever another creature enters, you gain 1 life.",
            },
        });
        var card = new ScryfallCardFactory(repo).Create("Soul Warden", alice);

        HasTriggerAbility(card).Should().BeTrue();
    }

    // Minimal repo for the synthetic tests.
    private sealed class TestRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _by;
        public TestRepo(Dictionary<string, CardEntity> by) { _by = by; }
        public CardEntity? GetByName(string name) => _by.TryGetValue(name, out var e) ? e : null;
        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(GetByName).OfType<CardEntity>().ToList();
        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) => _by.Values.ToList();
        public bool IsImplemented(string name) => _by.ContainsKey(name);
        public void SetImplemented(string name, bool value) { }
    }
}
```

- [ ] **Step 2: Run to verify the self-tests pass**

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter "FullyQualifiedName~TriggerWiringAuditTests"`
Expected: PASS — confirms `HasTriggerText` + `ScryfallCardFactory` wiring behave as expected. (If `Synthetic_TriggerText_WithAbility` fails, the binder/factory genuinely doesn't wire Soul Warden — that's a real finding; pick a known-wired card from `ScryfallFactoryTriggerWiringTests` instead.)

- [ ] **Step 3: Add the real-pool audit with an allowlist**

```csharp
// Cards whose oracle text matches a trigger pattern but legitimately bind no ITriggeredAbility
// (replacement effects "As ~ enters", purely-static text, reminder text, factory-owned non-trigger).
// Each entry MUST carry a reason. The audit fails only on NEW unexplained gaps.
private static readonly IReadOnlyDictionary<string, string> KnownNonTriggerCards =
    new Dictionary<string, string>
    {
        // ["Example Card"] = "Replacement effect (As ~ enters), not a trigger (CR 614).",
    };

[Fact]
public void RealPool_ImplementedCards_WithTriggerText_BindATrigger()
{
    var alice = new Player("Alice", 20);
    var repo = new EmbeddedCardRepository();
    var factory = new ScryfallCardFactory(repo);

    var implemented = repo.Search(q: null, implementedOnly: true, limit: 50000);

    var gaps = new List<string>();
    foreach (var entity in implemented)
    {
        if (!HasTriggerText(entity.OracleText)) continue;
        if (KnownNonTriggerCards.ContainsKey(entity.Name)) continue;

        Majik.Core.Cards.ICard card;
        try { card = factory.Create(entity.Name, alice); }
        catch { continue; } // build failures are a separate concern, not this audit's job
        if (card is null) continue;

        if (!HasTriggerAbility(card))
            gaps.Add($"{entity.Name} :: {Truncate(entity.OracleText)}");
    }

    gaps.Should().BeEmpty(
        "implemented cards with trigger text must bind a triggered ability "
        + "(add genuine non-triggers to KnownNonTriggerCards with a reason):\n"
        + string.Join("\n", gaps.Select(g => "  " + g)));
}

private static string Truncate(string? s) =>
    string.IsNullOrEmpty(s) ? "" : (s.Length <= 80 ? s : s.Substring(0, 80) + "…");
```

- [ ] **Step 4: Run the real-pool audit**

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter "FullyQualifiedName~TriggerWiringAuditTests.RealPool_ImplementedCards_WithTriggerText_BindATrigger"`
Expected: Either PASS, or FAIL listing real gaps. **A failure here is the audit working** — each listed card is either a genuine missing-trigger bug (fix the binder/factory in a follow-up) or a legitimate non-trigger (add to `KnownNonTriggerCards` with a reason). Triage every entry; do not blanket-allowlist. Land the allowlist needed to make the suite green and record any genuine bugs as follow-ups (append engine gaps to the v1-deferrals memory if they need new infra).

- [ ] **Step 5: Commit**

```bash
git add Majik.Core.Tests/CardData/TriggerWiringAuditTests.cs
git commit -s -m "test(audit): Class B static trigger-wiring audit over implemented pool"
```

---

## Task 10: Full-suite verification + PR

- [ ] **Step 1: Build + run both affected test projects**

Run:
```bash
dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj
dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter "FullyQualifiedName~TriggerWiringAuditTests"
dotnet build Majik.sln
```
Expected: green. Capture the bot-integration test count (should be 4 prior + new fuzz cases) and the audit result.

- [ ] **Step 2: Push the branch + open the PR**

```bash
git push -u origin fuzz-harness-trigger-audit
gh pr create --fill --base main
```
Per project workflow: auto-merge once `build-test` + `dco` checks are green.

- [ ] **Step 3: Update coverage docs if gaps were found**

If the Class B audit surfaced real missing-trigger cards, note them where the team tracks card gaps (`MODERN_COVERAGE.md` / v1-deferrals) so they're not lost.

---

## Self-Review Notes

- **Spec coverage:** crash/hang → Task 6 timeout + Task 7 `TimedOut` assert; invariants → Tasks 2–3 (zone, result, turn-cap); Class A → Task 4 (+ Task 5 suppression seam); Class B → Task 9; failure repro → Task 8; harness self-tests → Tasks 2/3/4 observer tests + Task 9 synthetic tests.
- **Deferred-to-implementation verifications** (flagged inline with `>` notes, each with a concrete fallback): exact `Zone`/`ZoneManager` member names; `GameDriver.GameResult` property names; `GameFacade.EventBus` concrete type; `EventType` enum member for the synthetic event; `SaveSnapshot` method name; the SBA-loop / stack-drain / priority-sanity invariants from the spec's table are **not yet** separate tasks — they are lower-value than the trigger work and the bot fixture decks rarely exercise them; add them as follow-on per-event checks in `OnEvent` using the same `_violations.Add` pattern if a fuzz run surfaces a need. This is a deliberate YAGNI scope cut, called out rather than silently dropped.
- **Type consistency:** `RunFinalChecks(int, string, string?, bool)` is the single final-check entry after Task 3; `InvariantViolation(Kind, Detail, Turn, Phase)` and `FuzzResult(...)` field names are used identically across Tasks 6–9.
