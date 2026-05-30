# PLAN 03 — Converge the four card-definition systems into one declarative model + shared primitives

## Goal
Collapse the four ways a card is defined into **one declarative model (the fluent `CardDef` DSL) backed by one effect/cost/trigger vocabulary (`Fx`/`Triggers`/`Costs`)**: JSON `CardDefinition` becomes a *serialization* of `CardDef`; JSON cards become **fileless** (~175 wrapper classes deleted); the DSL and JSON effect vocabularies stop being two catalogs; bespoke imperative factories shrink via discoverable helpers. The `Create(Player)` shim stays byte-compatible so **no call site moves** and in-flight card work keeps compiling. This is a refactor of *authoring*, not runtime semantics — the production `ScryfallCardFactory` binder path is untouched.

## Current state (file:line)
Four surfaces:
1. **Binder regex** — `CardData/*Binder.cs`, `OracleSpellBinder`, `SpellTemplates/` (125 templates). The **production** path (`ScryfallCardFactory.Create`; snapshot tests at `Majik.Core.Tests/Snapshots/ScryfallCardFactorySnapshotTests.cs`). Out of scope to converge, but it's the catalog of effect shapes to grow toward.
2. **Imperative `[CardName]` factories** (~928). Key finding: even DSL-opted factories (`ManaTitheFactory`) carry a **separate `BuildSpellDefinition()` returning a `SpellDefinition`** that `CardDef` does not own — two parallel resolve representations.
3. **Fluent `CardDef` DSL** (`Definitions/CardDef.cs:43`, `CardDefBuilder.cs:22`, `CardDefRuntime.cs:41`, `ResolveBuilder.cs:134`). Vocabulary = `ResolveEffectKind` enum (`ResolveBuilder.cs:65`) + `TargetKind` (`:13`). `MaterializeStep` (`CardDefRuntime.cs:149`) already partially routes through `Fx.*` (`:202,252`). ~177 factories expose `Define()`.
4. **JSON `CardDefinition`** (`Definitions/CardDefinition.cs:21`, `CardDefinitionFactory.cs:21`, `EffectDefinition.cs`, `CostDefinition.cs`, `TriggerDefinition.cs`). Separate vocabulary: `EffectDefinition` JSON union (`:15`), `CostDefinition` (`:11`), `TriggerDefinition` (`:11`, only `etb_self`/`card_leaves_your_graveyard`). 175 `*.json` under `CardData/Cards/` (embedded via `Majik.Core.csproj:13`), each needing a wrapper (`TranquilCoveFactory.cs:27`).

**Two effect vocabularies share zero code.** Both TODO toward a never-built `Effects/Primitives/` (`CardDef.cs:31-41`, `CardDefRuntime.cs:33-39`). But `Primitives/Fx.cs:45` and `Abilities/Triggers.cs:15` **already exist** as the de-facto primitive surface; there is **no `Costs` helper** (JSON inlines `AdditionalCost.*` in `CardDefinitionFactory.cs:232-282`; 122 factories reach `AdditionalCost.*` directly). Source-gen (`NamedCardFactoryGenerator.cs:84`) already detects `Define() : CardDef` (`:155`) and synthesizes `CardDefRuntime.Build(Define(), owner)` (`:331`) — but doesn't scan loose JSON files. `IsImplemented` derives from `[CardName]` reflection — removing wrappers must not drop names.

## Design
**Convergence surface: `CardDef`** (generator already treats `Define()` as fileless; clean builder; JSON is naturally its serialization). JSON `CardDefinition` gains `ToCardDef()`; `CardDefinitionFactory.Build` becomes `CardDefRuntime.Build(def.ToCardDef(), owner)`.

**One vocabulary:** both `CardDefRuntime.MaterializeStep` and `CardDefinitionFactory.BuildEffect` compile to the same `Fx.*`/`Costs.*`/`Triggers.*` calls. Keep `Majik.Core.Primitives.Fx` as canonical home (lowest churn; update stale TODOs); add a `Costs` helper (the set `BuildCost :232` inlines); keep `ResolveEffect` (`ResolveBuilder.cs:84`) as the canonical in-memory unit and map JSON `EffectDefinition` → `ResolveEffect`. The `*_stub` JSON variants map to real kinds once targeting lands (PLAN 01) or to a no-op until then.

**Async-awareness (depends on PLAN 01):** the unified materializer must target PLAN 01's async `IEffect` (today `CardDefinitionFactory.cs:402,433` does `.GetAwaiter().GetResult()` for scry/surveil) so convergence doesn't entrench a second sync-over-async layer.

**Fileless JSON:** extend the source generator to consume `Cards/*.json` as `AdditionalFiles` and emit a dispatch arm per `name`; feed JSON names into `ImplementedCardNames`; delete the 175 wrappers (JSON files stay `EmbeddedResource`; only wrappers go).

## Step-by-step (sliced; each ships green)
- **Slice 0 —** update stale TODO comments to point at the real `Fx`/`Triggers` home; document convergence direction in code.
- **Slice 1 —** add `Primitives/Costs.cs`; audit `Fx`/`Triggers` against the `SpellTemplates` catalog + JSON verbs and add missing primitives (async-ready); route `MaterializeStep` + `BuildEffect/BuildCost/BuildTrigger` through `Fx`/`Costs`/`Triggers` (internal only; tests stay green).
- **Slice 2 —** `CardDefinition.ToCardDef()` (extend `CardDef`/`CardDefBuilder` with an `Abilities` collection); reduce `CardDefinitionFactory.Build` to `CardDefRuntime.Build(def.ToCardDef(), owner)`; JSON union survives as wire format via `ToResolveEffect()`/`ToCost()`/`ToTrigger()`.
- **Slice 3 —** source-gen scans `Cards/*.json` (register glob as `AdditionalFiles`) → dispatch arm per name; teach `ImplementedCardNames` to include JSON names; delete the 175 wrappers.
- **Slice 4 —** opportunistic: bespoke factories drop namespace-soup for `Fx.*`/`Costs.*`. Non-forcing; leave the `SpellDefinition`/`BuildSpellResolveEffects` split for v1 (reconciling the two resolve reps is a follow-on gated on PLAN 01).

## Migration
177 DSL `Define()` factories: zero file changes (vocabulary re-pointed internally). 175 JSON wrappers: deleted in Slice 3 (JSON files stay; generator replaces wrapper). ~928 bespoke factories: not forced; shrink voluntarily in Slice 4. Binder/`SpellTemplates`: untouched.

## Test strategy
Snapshot tests (`ScryfallCardFactorySnapshotTests`, the binder/production path) must not diff — run after every slice. Capture a golden `SnapshotSummary` of all 175 JSON + 177 DSL cards' runtime `ICard` output before Slices 1–3; assert byte-identical after. `ImplementedCardNames` count test guards the fileless-JSON name regression. Extend source-gen unit tests for JSON→arm emission + duplicate-name diagnostics. Full `dotnet test Majik.sln` green per slice.

## Risks
Two resolve representations on DSL cards (this plan unifies vocabulary, NOT the two resolve pipelines in v1 — gated on PLAN 01; flag clearly); `IsImplemented` regression on wrapper deletion (Slice 3 step + count test); source-gen `AdditionalFiles` plumbing fiddly (keep JSON as `EmbeddedResource` so runtime load unchanged); churn collision with the deferrals agent (biggest scheduling risk); async signature drift if PLAN 01 isn't settled first.

## Effort
Slice 0 ~0.5d; Slice 1 ~3–4d (the `SpellTemplates` coverage audit dominates); Slice 2 ~3d; Slice 3 ~3–4d (generator + AdditionalFiles); Slice 4 ongoing. Total core (0–3) ~2 working weeks. Medium-high effort, low runtime risk, high authoring-surface churn.

## Dependencies / sequencing
**Depends on PLAN 01** (design vocabulary async-aware). If #1 slips, Slices 0–1 can proceed (consolidate existing sync helpers); Slice 2+ waits for async `IEffect`. Internal order strict: vocab → JSON→CardDef → fileless JSON → helper extraction.

## Coordination with the deferrals agent
Changes how cards are authored; the worker pool opens auto-merged card PRs continuously (~12 live worktrees, each with a `CardData/Definitions/` copy). **The `Create(Player)` shim is sacrosanct** — never change its signature or the `[CardName]` contract. Slices 1–2 are internal-only — land during a lull (pool drained). **Slice 3 is disruptive** (deletes 175 files + touches `Majik.Core.csproj` + `ImplementedCardNames`) — coordinate a hard pool freeze, let open PRs merge, land Slice 3 atomically against a quiesced tree, resume. After Slice 2, tell the agent the preferred new-card path is DSL `Define()` or fileless JSON (no wrapper) — but don't rewrite their existing factories. Keep vocabulary changes additive so worker branches cut before the change still merge.

### Critical files
- `Majik.Core/CardData/Definitions/CardDefRuntime.cs`, `CardDefinitionFactory.cs`, `CardDefinition.cs`
- `Majik.Core/Primitives/Fx.cs`
- `Majik.Core.SourceGen/NamedCardFactoryGenerator.cs`
