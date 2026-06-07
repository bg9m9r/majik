# Bot-Deck Implementation Audit — design

**Date:** 2026-06-07
**Status:** approved (pre-implementation)

## Problem

Bot decks ship cards whose engine implementation is missing or partial. Today
these are found only by **playing** a deck and noticing a card do nothing (or
the wrong thing) — slow, incomplete, and dependent on which cards happen to be
drawn. Recent examples found by hand: Agatha's Soul Cauldron's ability-grant
static (deferred), Agatha's auto-targeting, Earthbend granting a wrong creature
subtype.

We want to **detect deferred/problematic bot-deck card effects statically**, so
gaps surface at PR time instead of in play.

## Goals

- A CI **gate** that fails when a bot-deck card is unimplemented or partial in a
  way not already recorded.
- A human-readable **report** to triage each deck's health without playing it.
- Cover four problem classes (A–D below), with explicit limits where automation
  is not possible.

## Non-goals

- Catalog-wide detection of silent-wrong-but-complete implementations (class D).
  That needs per-card golden tests, which cannot be auto-generated. We seed a
  convention and the keyword mechanics we have already verified.
- Replacing the 24-deck mirror smoke (it catches *crashes*; this catches
  *coverage*). They are complementary.

## Problem classes

| Class | Definition | Detection | Automatable |
|-------|-----------|-----------|-------------|
| A | Does-nothing **shell** — real oracle text, no enforcing abilities | `ICard.IsVanillaShell` (computed at card build) | Fully |
| B | **Implied trigger missing** — permanent whose oracle text leads with When/Whenever/At but binds zero `ITriggeredAbility` | regex heuristic + allowlist | Mostly (some false positives) |
| C | **Known partial** — deliberately incomplete (e.g. Agatha grant) | central registry, cross-referenced per deck | Documentation-driven |
| D | **Silent-wrong-but-complete** — does something, but wrong (e.g. Earthbend subtype) | per-keyword golden tests | No (hand-written) |

## Architecture

Static catalog audit (primary) + a prod registry + a runtime hook in the mirror
smoke (secondary) + per-keyword goldens.

### 1. Prod registry (class C source of truth)

`Majik.Core/CardData/KnownPartialImplementations.cs`

```csharp
public enum CardGapSeverity { Stub, Partial }   // Stub = does nothing; Partial = does some of it
public sealed record CardGap(CardGapSeverity Severity, string Reason);

public static class KnownPartialImplementations
{
    public static readonly IReadOnlyDictionary<string, CardGap> ByName; // card name -> gap
    public static bool TryGet(string name, out CardGap gap);
}
```

- Lives in **prod** (Majik.Core), not test, so the portal/runtime can later
  surface a "partial" badge and so it sits next to the card-data layer.
- `Stub` = a card that currently does nothing it should (a vanilla shell in a
  bot deck). `Partial` = a card that implements some of its text but has a
  documented gap (Agatha's ability-grant static).
- Seeded truthfully on first run so the gate is green; each entry carries a
  `Reason` (cross-reference `v1-deferrals` memory where relevant).

### 2. Static catalog audit (primary gate + report)

`Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs`

- **Materialization:** for every **distinct** card name across all
  `BotDeckCatalog.Archetypes` mainboards **and** sideboards
  (`BotDeckCatalog.GetSideboard`), build the real card via
  `new ScryfallCardFactory(new EmbeddedCardRepository()).Create(name, dummy)`.
  This runs the full binder/factory chain, so `IsVanillaShell` and the bound
  ability set are exactly what production sees. Build the repo + factory **once**
  for the whole test class (the seed is ~22k rows).
- **Detect per card:**
  - **Stub (A):** `card.IsVanillaShell == true`.
  - **MissingTrigger (B):** the card is a permanent (creature / artifact /
    enchantment / planeswalker / land), its seed `OracleText` has a line that
    *starts* with `When`, `Whenever`, or `At ` (anchored per-line to avoid
    matching mid-sentence reminder text), and `card.Abilities.OfType<ITriggeredAbility>()`
    is empty. Instants/sorceries are excluded (their effects resolve at cast
    time, not as `Card.Abilities`). A small in-test `TriggerHeuristicAllowlist`
    set suppresses known false positives (keyword-derived triggers,
    replacement-effect "when" phrasing). The allowlist holds **false positives
    only** — real gaps go in the prod registry.
- **Gate** `[Fact] BotDeckCards_HaveNoUnregisteredGaps`:
  - Any detected **Stub** or **MissingTrigger** whose name is **not** in
    `KnownPartialImplementations.ByName` → collect as **NEW GAP**.
  - Any registry entry with `Severity == Stub` that **is** a bot-deck card but is
    **not** currently detected as a stub → collect as **STALE** ("looks
    implemented now — remove or downgrade the registry entry").
  - Fail with a single grouped message listing NEW GAPs and STALE entries.
  - `Partial` entries are **not** auto-verified (they do something; there is no
    cheap signal that the *remaining* part is still missing) — they are
    documentation that also suppresses A/B noise where they overlap.
- **Report** `[Fact] PrintPerDeckHealth` (always passes): writes a per-deck
  table to `ITestOutputHelper` — for each deck, each distinct card with status
  `OK | Stub | Partial | MissingTrigger` and the reason. Run on demand:
  `dotnet test --filter PrintPerDeckHealth`.

### 3. Runtime hook (secondary)

Extend `BotDeck_MirrorMatch_PlaysGame_NoCrash`
(`Majik.Bot.Tests.Integration/BotVsBotGameTests.cs`):

- Subscribe to `UnimplementedCardEncounteredEvent` on the facade's event bus
  before the game runs.
- After the game: assert every shell name the bot actually *encountered* is in
  `KnownPartialImplementations.ByName` (defense-in-depth — the static audit
  already covers all cards, but this catches an in-context surprise), and echo
  the encountered names to test output.
- This is a light addition (capture + one assertion), not a new test.

### 4. Golden tests (class D)

Per-keyword behavior assertions live in per-keyword test files. Earthbend's
(`EarthbendActionTests`: grants Creature with **no** subtype, Haste, still a
Land, N/N from counters, **one-shot** return) already satisfy this and are the
seed example. Convention: when a keyword action is added or its behavior
verified against oracle text, add/extend its golden test. **Not catalog-wide**
— documented as the only mitigation for class D.

## Data flow

```
BotDeckCatalog.Archetypes ─┐
                           ├─> distinct card names (main + sideboard)
BotDeckCatalog.GetSideboard┘
        │
        v
ScryfallCardFactory.Create(name, dummy)  [real binders, EmbeddedCardRepository]
        │
        v
per-card signals: IsVanillaShell (A), oracle-vs-triggers (B)
        │
        ├─> Gate: diff {detected gaps} vs KnownPartialImplementations  -> pass/fail
        └─> Report: group by deck -> ITestOutputHelper
```

## Error handling / edge cases

- **Card missing from seed:** `BotDeckValidator` already guarantees bot-deck
  names resolve; if `Create` returns an unknown-name shell, that surfaces as a
  Stub (and is itself a signal worth failing on).
- **MDFC composite names ("Front // Back"):** `GetByName` resolves the composite
  entity; build as-is. If the heuristic mis-flags a face, allowlist it.
- **Heuristic false positives (B):** absorbed by `TriggerHeuristicAllowlist`;
  keep it small and commented per entry.
- **Lands with "When ~ enters tapped":** that is a replacement effect, not a
  triggered ability — covered by the allowlist / excluded by the anchored
  regex tuning.

## Testing

The audit *is* the test. Verify by:
1. Running the gate against the seeded baseline → green.
2. Temporarily removing a registry `Stub` entry → gate fails with NEW GAP.
3. Temporarily adding a bogus `Stub` entry for an implemented bot-deck card →
   gate fails with STALE.
4. `PrintPerDeckHealth` produces a readable per-deck breakdown.

## Files

- `Majik.Core/CardData/KnownPartialImplementations.cs` — new (prod registry).
- `Majik.Bot.Tests/Decks/BotDeckImplementationAuditTests.cs` — new (gate + report).
- `Majik.Bot.Tests.Integration/BotVsBotGameTests.cs` — edit (runtime hook).
- `Majik.Core.Tests/Keywords/EarthbendActionTests.cs` — existing (class-D seed; no change unless gaps found).

## Limitations (explicit)

- Catches **does-nothing**, **missing-trigger**, and **documented-partial**
  cards automatically. Does **not** catch silent-wrong-but-complete impls beyond
  hand-written goldens.
- Class B is a heuristic; the allowlist is maintenance the team accepts.
- Class C only surfaces gaps we remember to register — but the gate's
  stale-check keeps stub entries honest, and the report makes the registry
  visible per deck.
