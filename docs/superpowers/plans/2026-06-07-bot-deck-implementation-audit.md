# Bot-Deck Implementation Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Statically detect bot-deck cards that are unimplemented (do nothing), missing an implied trigger, or a documented partial — as a CI gate plus an on-demand per-deck health report — so coverage gaps surface at PR time instead of in play.

**Architecture:** A prod registry (`KnownPartialImplementations`) is the source of truth for known gaps. A unit-level audit test in `Majik.Bot.Tests` builds every distinct bot-deck card through the real `ScryfallCardFactory`, classifies each (`IsVanillaShell` → Stub; oracle-implies-trigger-but-none-bound → MissingTrigger), and **gates** on drift versus the registry; a second always-passing test prints a per-deck health report. The 24-deck mirror smoke gains a secondary runtime hook asserting any shell the bot actually draws is registered.

**Tech Stack:** C# / .NET 10, xUnit + FluentAssertions, `Majik.Core` (`ScryfallCardFactory`, `EmbeddedCardRepository`, `ICard.IsVanillaShell`), `Majik.Bot` (`BotDeckCatalog`).

---

## File structure

- `Majik.Core/CardData/KnownPartialImplementations.cs` — **new**. Prod registry: `CardGapSeverity` enum, `CardGap` record, `ByName` dict, `TryGet`.
- `Majik.Core.Tests/CardData/KnownPartialImplementationsTests.cs` — **new**. Unit test for the registry.
- `Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs` — **new**. Materialization + detection helpers + gate `[Fact]` + report `[Fact]`.
- `Majik.Bot.Tests.Integration/BotVsBotGameTests.cs` — **modify**. Runtime hook in the mirror smoke.

---

## Task 1: Prod registry `KnownPartialImplementations`

**Files:**
- Create: `Majik.Core/CardData/KnownPartialImplementations.cs`
- Test: `Majik.Core.Tests/CardData/KnownPartialImplementationsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Majik.Core.Tests/CardData/KnownPartialImplementationsTests.cs`:

```csharp
using FluentAssertions;
using Majik.Core.CardData;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class KnownPartialImplementationsTests
{
    [Fact]
    public void Registry_RecordsAgatha_AsPartial()
    {
        KnownPartialImplementations.TryGet("Agatha's Soul Cauldron", out var gap)
            .Should().BeTrue("Agatha's ability-grant static is a documented partial");
        gap.Severity.Should().Be(CardGapSeverity.Partial);
        gap.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryGet_UnknownCard_ReturnsFalse()
    {
        KnownPartialImplementations.TryGet("Definitely Not A Real Card", out _)
            .Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter "FullyQualifiedName~KnownPartialImplementationsTests"`
Expected: FAIL — `KnownPartialImplementations` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `Majik.Core/CardData/KnownPartialImplementations.cs`:

```csharp
namespace Majik.Core.CardData;

/// <summary>How incomplete a card's implementation is.</summary>
public enum CardGapSeverity
{
    /// <summary>The card currently does nothing it should — a vanilla shell in
    /// a bot deck (<see cref="Majik.Core.Cards.ICard.IsVanillaShell"/>).</summary>
    Stub,

    /// <summary>The card implements some of its text but has a documented gap
    /// (e.g. Agatha's Soul Cauldron's ability-grant static).</summary>
    Partial,
}

/// <summary>One known implementation gap for a card.</summary>
public sealed record CardGap(CardGapSeverity Severity, string Reason);

/// <summary>
/// Machine-readable registry of cards with a KNOWN implementation gap. The
/// bot-deck implementation audit (<c>BotDeckImplementationAuditTests</c>) gates
/// against this: a newly-detected Stub / MissingTrigger card that is NOT here
/// fails the build, and a <see cref="CardGapSeverity.Stub"/> entry that is no
/// longer detected as a shell fails as "stale". Lives in prod (not test) so the
/// portal/runtime can later surface a "partial coverage" badge.
///
/// <para><see cref="CardGapSeverity.Partial"/> entries are documentation only —
/// the card does something, so there is no cheap signal that the remaining part
/// is still missing.</para>
/// </summary>
public static class KnownPartialImplementations
{
    public static readonly IReadOnlyDictionary<string, CardGap> ByName =
        new Dictionary<string, CardGap>(StringComparer.Ordinal)
        {
            ["Agatha's Soul Cauldron"] = new CardGap(
                CardGapSeverity.Partial,
                "Ability-grant static deferred (closure re-home blocker, v1-deferrals #5); "
                + "real targeting + Legendary supertype done (#2497)."),
            // Further entries are seeded in Task 3 from the first audit run.
        };

    /// <summary>True when <paramref name="name"/> has a recorded gap.</summary>
    public static bool TryGet(string name, out CardGap gap)
        => ByName.TryGetValue(name, out gap!);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter "FullyQualifiedName~KnownPartialImplementationsTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Majik.Core/CardData/KnownPartialImplementations.cs Majik.Core.Tests/CardData/KnownPartialImplementationsTests.cs
git commit -s -m "feat(carddata): KnownPartialImplementations registry"
```

---

## Task 2: Audit detection + per-deck report

**Files:**
- Create: `Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs`

This task adds the materialization + detection helpers and the always-passing
**report** test. The failing **gate** test is added in Task 3 (after the
registry is seeded from this report's output, so the build is never red between
commits).

- [ ] **Step 1: Write the report test + helpers**

Create `Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs`:

```csharp
using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Static coverage audit for every card in every bot deck (mainboard +
/// sideboard). Builds each distinct card through the real
/// <see cref="ScryfallCardFactory"/> (full binder/factory chain, same as
/// production) and classifies it. The gate fails on drift versus
/// <see cref="KnownPartialImplementations"/>; the report prints a per-deck
/// breakdown.
///
/// Class D (silent-wrong-but-complete impls, e.g. a keyword that grants the
/// wrong subtype) is NOT covered here — those need per-keyword golden tests
/// (see <c>EarthbendActionTests</c> as the seed example).
/// </summary>
public class BotDeckImplementationAuditTests
{
    private readonly ITestOutputHelper _out;
    public BotDeckImplementationAuditTests(ITestOutputHelper output) => _out = output;

    // Built once for the whole class — the seed is ~22k rows.
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);
    private static readonly Player Dummy = new("Audit", 20);

    /// <summary>Class-B heuristic false positives: oracle text leads with
    /// When/Whenever/At, but the "trigger" is actually a keyword or replacement
    /// effect, not an <see cref="ITriggeredAbility"/>. Real gaps go in
    /// <see cref="KnownPartialImplementations"/>, NOT here. Seeded in Task 3.</summary>
    private static readonly HashSet<string> TriggerHeuristicAllowlist =
        new(StringComparer.Ordinal)
        {
            // Seeded in Task 3 from the first audit run.
        };

    /// <summary>Raw detection result, ignoring the registry.</summary>
    private enum RawSignal { None, Stub, MissingTrigger }

    /// <summary>Report-facing status (raw signal overlaid with the registry).</summary>
    private enum Status { Ok, Stub, Partial, MissingTrigger }

    private static IReadOnlyList<string> AllBotDeckCardNames()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            foreach (var n in BotDeckCatalog.Get(archetype)) names.Add(n);
            foreach (var n in BotDeckCatalog.GetSideboard(archetype)) names.Add(n);
        }
        return names.ToList();
    }

    private static bool OracleImpliesTrigger(string? oracle)
    {
        if (string.IsNullOrWhiteSpace(oracle)) return false;
        foreach (var raw in oracle.Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("When ", StringComparison.Ordinal)
                || line.StartsWith("Whenever ", StringComparison.Ordinal)
                || line.StartsWith("At ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsPermanent(ICard c)
        => c.HasType(CardType.Creature) || c.HasType(CardType.Artifact)
        || c.HasType(CardType.Enchantment) || c.HasType(CardType.Planeswalker)
        || c.HasType(CardType.Land);

    /// <summary>Pure detection — does NOT consult the registry.</summary>
    private static RawSignal DetectRaw(string name)
    {
        var card = Factory.Create(name, Dummy);
        if (card.IsVanillaShell) return RawSignal.Stub;

        var entity = Repo.GetByName(name);
        if (entity != null
            && IsPermanent(card)
            && OracleImpliesTrigger(entity.OracleText)
            && !card.Abilities.OfType<ITriggeredAbility>().Any()
            && !TriggerHeuristicAllowlist.Contains(name))
            return RawSignal.MissingTrigger;

        return RawSignal.None;
    }

    /// <summary>Report status: registry overlay over the raw signal.</summary>
    private static Status ReportStatus(string name)
    {
        if (KnownPartialImplementations.TryGet(name, out var gap))
            return gap.Severity == CardGapSeverity.Stub ? Status.Stub : Status.Partial;

        return DetectRaw(name) switch
        {
            RawSignal.Stub => Status.Stub,
            RawSignal.MissingTrigger => Status.MissingTrigger,
            _ => Status.Ok,
        };
    }

    [Fact]
    public void PrintPerDeckHealth()
    {
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            var names = BotDeckCatalog.Get(archetype)
                .Concat(BotDeckCatalog.GetSideboard(archetype))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var problems = names
                .Select(n => (Name: n, Status: ReportStatus(n)))
                .Where(x => x.Status != Status.Ok)
                .ToList();

            _out.WriteLine($"=== {BotDeckCatalog.DisplayName(archetype)} "
                + $"({problems.Count}/{names.Count} flagged) ===");
            foreach (var (n, status) in problems)
            {
                var reason = KnownPartialImplementations.TryGet(n, out var gap)
                    ? gap.Reason : "(detected — not yet registered)";
                _out.WriteLine($"  [{status}] {n} — {reason}");
            }
        }
    }
}
```

- [ ] **Step 2: Run the report to harvest the current gap list**

Run: `dotnet test Majik.Bot.Tests/Majik.Bot.Tests.csproj --filter "FullyQualifiedName~BotDeckImplementationAuditTests.PrintPerDeckHealth" --logger "console;verbosity=detailed"`
Expected: PASS. Read the console output and **record every line tagged
`(detected — not yet registered)`** — these are the un-registered Stub /
MissingTrigger cards. This list feeds Task 3. Save it (e.g. paste into the PR
description scratch area).

- [ ] **Step 3: Commit**

```bash
git add Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs
git commit -s -m "feat(bot-tests): bot-deck coverage detection + per-deck health report"
```

---

## Task 3: Seed the registry/allowlist and add the gate

**Files:**
- Modify: `Majik.Core/CardData/KnownPartialImplementations.cs` (add seeded entries)
- Modify: `Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs` (seed allowlist + add gate `[Fact]`)

- [ ] **Step 1: Triage each harvested card and seed the two lists**

For every card from Task 2 Step 2's harvested list, decide:
- **Genuinely does nothing / missing a real trigger** → add to
  `KnownPartialImplementations.ByName` with the correct `CardGapSeverity`
  (`Stub` for a vanilla shell, `Partial` if it does some of its text) and a
  one-line `Reason` (reference `v1-deferrals` where applicable).
- **Class-B false positive** (the When/Whenever/At line is a keyword or
  replacement effect, not a real triggered ability) → add the card name to
  `TriggerHeuristicAllowlist` with an inline comment saying why.

Edit `KnownPartialImplementations.ByName` — append entries in this exact shape
(one real example shown; repeat per harvested Stub/Partial card):

```csharp
            ["<Card Name From Harvest>"] = new CardGap(
                CardGapSeverity.Stub,
                "<why it does nothing — e.g. 'no factory; oracle text unenforced'>."),
```

Edit `TriggerHeuristicAllowlist` — append per false-positive card:

```csharp
            "<Card Name>", // <why this When/At line is not a triggered ability>
```

- [ ] **Step 2: Add the gate test**

Add to `BotDeckImplementationAuditTests` (below `PrintPerDeckHealth`):

```csharp
    [Fact]
    public void BotDeckCards_HaveNoUnregisteredGaps()
    {
        var botDeckNames = AllBotDeckCardNames();
        var newGaps = new List<string>();
        var stale = new List<string>();

        foreach (var name in botDeckNames)
        {
            var raw = DetectRaw(name);
            var known = KnownPartialImplementations.TryGet(name, out var gap);

            // A detected gap that nobody recorded → fail (implement it, or
            // register it with a reason if the gap is intentional).
            if (raw != RawSignal.None && !known)
            {
                newGaps.Add(raw == RawSignal.Stub
                    ? $"{name}: does nothing (vanilla shell) — implement, or register as Stub"
                    : $"{name}: oracle implies a trigger but none is bound — implement, "
                      + "register as a gap, or allowlist the heuristic false positive");
            }

            // A registry Stub entry that is no longer a shell → fail (clean it up).
            if (known && gap.Severity == CardGapSeverity.Stub && raw != RawSignal.Stub)
            {
                stale.Add($"{name}: registered as Stub but is no longer a vanilla shell "
                    + "— remove or downgrade the registry entry");
            }
        }

        var failures = newGaps.Concat(stale).ToList();
        failures.Should().BeEmpty(
            "bot-deck cards must be implemented or have their gap recorded in "
            + "KnownPartialImplementations / the trigger-heuristic allowlist. "
            + "Run PrintPerDeckHealth for the full picture.\n"
            + string.Join("\n", failures));
    }
```

- [ ] **Step 3: Run the gate to verify it is green on the seeded baseline**

Run: `dotnet test Majik.Bot.Tests/Majik.Bot.Tests.csproj --filter "FullyQualifiedName~BotDeckImplementationAuditTests"`
Expected: PASS (both `PrintPerDeckHealth` and `BotDeckCards_HaveNoUnregisteredGaps`).
If `BotDeckCards_HaveNoUnregisteredGaps` fails, the message lists the exact
remaining cards — resolve each by registering it or allowlisting it (Step 1),
then re-run.

- [ ] **Step 4: Verify the gate actually catches drift (sanity)**

Temporarily delete the `Agatha's Soul Cauldron` entry from
`KnownPartialImplementations.ByName`. Agatha is NOT a vanilla shell, so it will
NOT be reported as a `newGap` (it has abilities) — instead confirm the gate's
NEW-GAP path with a true shell: temporarily add a bogus allowlist removal isn't
needed. Concretely: temporarily set `TriggerHeuristicAllowlist` to empty and
re-run the gate — expect FAIL listing any MissingTrigger cards you allowlisted.
Then restore. (This confirms the gate fails on un-recorded detections.)

Run: `dotnet test Majik.Bot.Tests/Majik.Bot.Tests.csproj --filter "FullyQualifiedName~BotDeckImplementationAuditTests.BotDeckCards_HaveNoUnregisteredGaps"`
Expected: FAIL while the allowlist is empty (if any were allowlisted); PASS after restoring.

- [ ] **Step 5: Restore and commit**

```bash
git add Majik.Core/CardData/KnownPartialImplementations.cs Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs
git commit -s -m "feat(bot-tests): gate bot-deck coverage against KnownPartialImplementations + seed baseline"
```

---

## Task 4: Runtime hook in the mirror smoke

**Files:**
- Modify: `Majik.Bot.Tests.Integration/BotVsBotGameTests.cs`

Subscribe to `UnimplementedCardEncounteredEvent` during the 24-deck mirror
smoke (via the established `VanillaShellTracker` + shared `EventBus` pattern)
and assert any shell the bot actually draws is registered.

- [ ] **Step 1: Add the runtime-hook assertion to the mirror test**

In `Majik.Bot.Tests.Integration/BotVsBotGameTests.cs`, add these usings if
absent:

```csharp
using Majik.Core.CardData;
using Majik.Core.Diagnostics;
using Majik.Core.Events;
```

Replace the body of `BotDeck_MirrorMatch_PlaysGame_NoCrash` with the version
below (adds the shared bus + trackers + post-game assertion; the rest is
unchanged):

```csharp
    [Theory]
    [MemberData(nameof(AllArchetypes))]
    public async Task BotDeck_MirrorMatch_PlaysGame_NoCrash(string archetype)
    {
        var facade = GameFacade.Create(
            aliceName: $"{archetype}-A",
            bobName:   $"{archetype}-B",
            aliceDeck: DeckLoader.LoadReal(archetype, Repo),
            bobDeck:   DeckLoader.LoadReal(archetype, Repo),
            cardRepo:  Repo);

        // Runtime coverage hook: capture every vanilla-shell card the bots
        // actually encounter. The facade's bus isn't directly subscribable for
        // raw GameEvents, so use the VanillaShellTracker + shared-bus pattern.
        var encountered = new List<string>();
        var sharedBus = new EventBus();
        sharedBus.Subscribe<UnimplementedCardEncounteredEvent>(e =>
        {
            lock (encountered) encountered.Add(e.CardName);
        });
        var aliceTracker = new VanillaShellTracker(sharedBus, _ => { });
        var bobTracker = new VanillaShellTracker(sharedBus, _ => { });

        facade.ReplaceAliceAgent(new BotPlayerAgent(facade.Alice,
            new BotConfig(archetype, RandomSeed: 1, VanillaShellTracker: aliceTracker)));
        facade.ReplaceBobAgent(new BotPlayerAgent(facade.Bob,
            new BotConfig(archetype, RandomSeed: 2, VanillaShellTracker: bobTracker)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await facade.StartFullGameAsync(maxTurns: 20, ct: cts.Token);
        await facade.FullGameTask!;
        facade.FullGameTask!.IsCompletedSuccessfully.Should().BeTrue(
            $"the '{archetype}' mirror match must run to the turn cap without crashing");

        // Any shell the bot drew must be a recorded gap — an UNregistered shell
        // surfacing in real play is exactly what we want to catch.
        var unregistered = encountered
            .Distinct(StringComparer.Ordinal)
            .Where(n => !KnownPartialImplementations.TryGet(n, out _))
            .ToList();
        unregistered.Should().BeEmpty(
            $"every vanilla-shell card the '{archetype}' bots encountered must be "
            + "in KnownPartialImplementations: " + string.Join(", ", unregistered));
    }
```

- [ ] **Step 2: Build the integration project**

Run: `dotnet build Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Run the mirror smoke**

Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~BotDeck_MirrorMatch" --no-build`
Expected: PASS (24/24). If a deck fails on `unregistered`, the message names the
shell(s) — register them in `KnownPartialImplementations` (Task 3 Step 1) and
re-run.

- [ ] **Step 4: Commit**

```bash
git add Majik.Bot.Tests.Integration/BotVsBotGameTests.cs
git commit -s -m "test(bot): assert mirror-smoke shell encounters are all registered gaps"
```

---

## Task 5: Document the class-D golden convention

**Files:**
- Modify: `Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs` (class xmldoc already references it — verify/extend)

Class D (silent-wrong-but-complete impls) is intentionally NOT auto-detected.
Make the convention discoverable so future keyword work adds goldens.

- [ ] **Step 1: Confirm the seed golden exists**

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter "FullyQualifiedName~EarthbendActionTests"`
Expected: PASS — these assert Earthbend's exact behavior (no subtype, haste,
still a land, N/N, one-shot return). This is the class-D seed example.

- [ ] **Step 2: Ensure the audit class xmldoc documents the convention**

Confirm the `BotDeckImplementationAuditTests` class summary (written in Task 2)
contains the sentence pointing to per-keyword golden tests + `EarthbendActionTests`
as the seed. If missing, add it. No behavioral code — documentation only.

- [ ] **Step 3: Commit (only if the xmldoc changed)**

```bash
git add Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs
git commit -s -m "docs(bot-tests): document class-D keyword-golden convention"
```

---

## Final verification

- [ ] **Run the full affected suites**

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj` → expect PASS.
Run: `dotnet test Majik.Bot.Tests/Majik.Bot.Tests.csproj` → expect PASS.
Run: `dotnet test Majik.Bot.Tests.Integration/Majik.Bot.Tests.Integration.csproj --filter "FullyQualifiedName~BotDeck_MirrorMatch"` → expect PASS (24/24).

- [ ] **Open PR** with auto-merge once CI is green (per repo workflow).

---

## Self-review notes

- **Spec coverage:** registry (§1) → Task 1; static audit + report (§2) → Tasks 2–3; runtime hook (§3) → Task 4; class-D convention (§4) → Task 5. Gate drift (new + stale) → Task 3 Step 2. Limitations preserved (class D not auto-detected).
- **Type consistency:** `CardGapSeverity {Stub,Partial}`, `CardGap(Severity,Reason)`, `KnownPartialImplementations.{ByName,TryGet}`, audit `RawSignal {None,Stub,MissingTrigger}` / `Status {Ok,Stub,Partial,MissingTrigger}` / `DetectRaw` / `ReportStatus` / `AllBotDeckCardNames` / `TriggerHeuristicAllowlist` — used consistently across Tasks 2–4.
- **Baseline-never-red:** report (Task 2) precedes the gate (Task 3), and the registry is seeded before the gate `[Fact]` is added, so no intermediate commit is red.
