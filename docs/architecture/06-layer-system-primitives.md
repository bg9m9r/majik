# PLAN 06 — Fill layer-system primitives (Layer-5 colors, public supertype-grant, Enchantment-Creature dual-type)

> **OWNERSHIP: this is the deferrals agent's work.** Items map 1:1 to open v1-deferrals #17 (Leyline of the Guildpact color), #4 (Boromir ring-bearer supertype), #10 (Enchantment-Creature dual-type). This document is the **primitive design hand-off**, not an independent implementation track. Do NOT have two agents touch `Card.cs` / `PermanentCharacteristics.cs` / `ContinuousEffectsService.cs` concurrently.

## Goal
Supply the three missing CR-613 layer primitives. The layer engine (`ContinuousEffectsService`, `GrantAbilityEffect`, `Layer4TypeStripEffect`) is sound; these are additive primitives, each S-sized.

## Current state (file:line)
- **Layer 5 (color) absent.** `Majik.Core/Effects/Layer.cs:18-19` defines `Color = 5` but no effect targets it and `PermanentCharacteristics` (`PermanentCharacteristics.cs:8-13`) has no `Colors`. Documented at `OkoThiefOfCrownsFactory.cs:62-64` ("no `SetColorsEffect` exists yet — Layer 5 colour-changing is absent"). Color today is static via `CardColors.GetColors` (`CardColors.cs:34-101`), never consulting layers.
- **Supertype-grant has no public mutator.** `Card._supertypes` private (`Card.cs:18`), read-only at `:62`, `HasSupertype` at `:1003`; `internal void AddCardType` at `:992` but no `AddSupertype`, and no Layer-4 supertype-grant effect. `CardSupertype` at `Types/CardSupertype.cs`.
- **Enchantment-Creature dual-type ad hoc.** Heliod (`HeliodSunCrownedFactory.cs:27-30`) builds a `Creature` then stamps `CardType.Enchantment` via `Card.AddCardType`. Works, but copy-pasted per factory, and count-predicates (devotion in Heliod/`SterlingGroveFactory`/`SanctumWeaverFactory`/`SolemnityFactory`/`HollowOneFactory`) read `HasType` inconsistently.
- **Pipeline they plug into:** `ContinuousEffectsService.Compute` (`:66-155`) seeds printed types/subtypes/keywords (`:85-93`) then applies `byLayer` groups. `Layer4TypeStripEffect.cs` is the model Layer-4 effect; `ContinuousEffect.cs` base defines `Layer`/`AppliesTo`/`Apply`/`Source`/`DependsOn`.

## Design (three primitives, each S)
**P1 — Layer-5 colors (#17):** add `HashSet<ManaColor> Colors` to `PermanentCharacteristics`; seed from `CardColors.GetColors(card)` in `Compute` after type seeding; new `SetColorsEffect : ContinuousEffect` (`Layer.Color`) with **set** (Oko green Elk) and **add-all** ({WUBRG}, Leyline of the Guildpact, predicate-scoped `AppliesTo`) modes; add `ContinuousEffectsService.EffectiveColors(perm)` (parallel to `EffectiveController` `:403`) and route color predicates through it with a null-fallback to the static path. Register a green `SetColorsEffect` in Oko.

**P2 — public supertype-grant (#4):** add `internal void AddSupertype(CardSupertype)` to `Card` (mirror `AddCardType:992`); add `HashSet<CardSupertype> Supertypes` to characteristics + seed; new `GrantSupertypeEffect : ContinuousEffect` at `Layer.Type` (CR 205.4/613.1d) adding the granted supertype; expose `EffectiveSupertypes(perm)` and point the legend-rule SBA at it.

**P3 — Enchantment-Creature dual-type uniformity (#10):** add a `PermanentBuilders.EnchantmentCreature(...)` helper (DRY the additive stamp); route the five devotion/count predicates through the computed type set (`Compute(perm).Types` already holds both types from the seed) rather than the concrete subclass. No new effect class.

## Step-by-step
1. (P1) `Colors` on characteristics + seed. **S.**
2. (P1) `SetColorsEffect` (set/add-all) + `EffectiveColors` + route predicates; register in Oko. **S.**
3. (P2) `Card.AddSupertype` + `Supertypes` on characteristics + seed. **S.**
4. (P2) `GrantSupertypeEffect` (Layer 4) + `EffectiveSupertypes` + legend-rule SBA. **S.**
5. (P3) `EnchantmentCreature` builder + route 5 count predicates through computed types. **S.**

## Test strategy
P1: Leyline add-all → every permanent {WUBRG}; Oko set-green visible to a "green creature" predicate; printed colors restored on source LTB. P2: granting `Legendary` triggers legend-rule SBA; revoked on source leave. P3: Enchantment-Creature counts toward both "creatures" and "enchantments" (Heliod devotion regression net). All under `Majik.Core.Tests/Effects/`, `TestDataBuilder`.

## Risks
Color predicate fan-out (preserve static fallback for no-effect/non-battlefield reads); layer ordering (supertype touches `Supertypes`, strip touches `Types` — independent in topo-sort); over-building P3 (it's predicate plumbing, not a new effect).

## Effort
S each; total S–M. Independent of PLAN 01 (no `IEffect`/agent/targeting surface). P1/P2/P3 mutually independent.

## Coordination with the deferrals agent — CRITICAL
These three ARE the deferrals agent's open items (#17/#4/#10), in the same single-owner hotspot files. **The deferrals agent implements #6**; this doc is the spec only. Two agents touching colors/supertype/dual-type concurrently guarantees conflicts across their ~12 worktrees.

### Critical files
- `Majik.Core/Effects/PermanentCharacteristics.cs`, `ContinuousEffectsService.cs`, `Layer4TypeStripEffect.cs`
- `Majik.Core/Cards/Card.cs`, `CardColors.cs`
