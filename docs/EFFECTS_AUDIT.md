# Effects-primitive audit

Snapshot of effect "verbs" used inside `Majik.Core/CardData/Factories/*.cs` resolve bodies, taken at branch `feat/effects-primitives`.

288 factory files were scanned with `grep` over each verb form. Counts below are raw occurrences and the number of distinct factory files that touch the verb at least once. Tally is descriptive — duplicates (e.g. a factory that calls `OracleSpellBinder.DealDamage` and `DealDamageWithPlaneswalker`) are counted once per match per file.

## Top 20 verbs by file fan-out

| Rank | Verb | Uses | Files | Canonical home |
|---:|---|---:|---:|---|
| 1 | `Sacrifice` (string match — incl. doc) | 199 | 34 | various; no single primitive — sacrifice is owner-routed graveyard move |
| 2 | `Zones.Hand.AddCard` | 61 | 56 | `ZoneService.MoveCard` / new `Effects.Primitives.Zones.MoveTo*` |
| 3 | `Discard` (token match — incl. discard cost types) | 55 | 14 | `DiscardACardCost` + new `Effects.Primitives.Hand.DiscardN` |
| 4 | `MarkTriedToDrawFromEmptyLibrary` | 47 | 24 | wrapped by new `Effects.Primitives.Cards.DrawCards` |
| 5 | `OracleSpellBinder.MoveToGraveyard` | 42 | 17 | aliased as `Effects.Primitives.Zones.MoveToGraveyard` |
| 6 | `Player.LoseLife` | 42 | 20 | aliased as `Effects.Primitives.Life.Lose` |
| 7 | `Zones.Graveyard.AddCard` | 38 | 32 | `ZoneService.MoveCard` / `Zones.MoveToGraveyard` |
| 8 | `PumpUntilEndOfTurnEffect` (incl. nested class) | 38 | 15 | aliased as `Effects.Primitives.Combat.PumpEot` |
| 9 | `TokenFactory.CreateOnBattlefield` | 37 | 15 | aliased as `Effects.Primitives.Tokens.Create*` |
| 10 | `OracleSpellBinder.RemoveFromStack` | 33 | 20 | aliased as `Effects.Primitives.Stack.Counter` |
| 11 | `OracleSpellBinder.DealDamage` | 32 | 18 | aliased as `Effects.Primitives.Damage.Deal` |
| 12 | `Zones.Battlefield.AddCard` | 30 | 29 | `ZoneService.MoveCard` |
| 13 | `Zones.Exile.AddCard` | 29 | 25 | `OracleSpellBinder.MoveToExile` / `Effects.Primitives.Zones.MoveToExile` |
| 14 | `ScryAction.*` | 25 | 4 | aliased as `Effects.Primitives.Library.Scry` |
| 15 | `Player.GainLife` | 24 | 14 | aliased as `Effects.Primitives.Life.Gain` |
| 16 | `Counters.Add` | 20 | 19 | aliased as `Effects.Primitives.Counters.Place` |
| 17 | `GrantKeyword*` | 19 | 7 | existing per-keyword grant effects — passthrough |
| 18 | `Creature.TakeDamage` (direct call) | 15 | 8 | covered by `Damage.Deal` (Creature dispatch) |
| 19 | `SurveilAction.*` | 13 | 3 | aliased as `Effects.Primitives.Library.Surveil` |
| 20 | `DealDamageWithPlaneswalker` | 10 | 5 | aliased as `Effects.Primitives.Damage.DealAny` |

(`MillAction.Apply` is 8 uses across 4 files; aliased as `Effects.Primitives.Library.Mill`.)

## Headline finding

The verb fan-out is **already mostly absorbed by existing helpers**: `OracleSpellBinder.{DealDamage,MoveToGraveyard,MoveToExile,RemoveFromStack}`, `MillAction.Apply`, `ScryAction` / `SurveilAction`, `TokenFactory.Create*`, `ZoneService.MoveCard`, plus `Player.{GainLife,LoseLife,MarkTriedToDrawFromEmptyLibrary,GainEnergy,PayEnergy,AddPoisonCounters}`, `Permanent.Counters.Add`, `Planeswalker.RemoveLoyalty`. The top ~10 verbs collectively cover the 80% mark for what an instant/sorcery resolve body actually does.

What's missing isn't another reimplementation — it's a **single canonical import surface**. Today a new factory has to know:

- where `DealDamage` lives (`Majik.Core.CardData.OracleSpellBinder`),
- where the planeswalker-aware variant lives (`Majik.Core.CardData.Factories.SearingBlazeFactory.DealDamageWithPlaneswalker`),
- where `Mill` lives (`Majik.Core.Keywords.MillAction`),
- where `Scry` / `Surveil` live (`Majik.Core.Keywords`),
- where token factories live (`Majik.Core.Tokens.TokenFactory`),
- where `MoveToGraveyard` / `MoveToExile` / `RemoveFromStack` live (still `OracleSpellBinder`, despite Reanimate / Animate Dead / etc. using raw `ZoneService.MoveCard`).

…before they can write a resolve body.

## Decision

Ship a thin **facade** namespace `Majik.Core.Effects.Primitives` that **re-exports** the canonical implementations under one ergonomic surface (`Damage`, `Life`, `Zones`, `Library`, `Tokens`, `Stack`, `Counters`, `Hand`, `Cards`) plus the few small primitives that don't have a home yet (`Cards.DrawCards`, `Hand.DiscardN`, `Library.LookAtTopN`). Factories opt in by adding `using Majik.Core.Effects.Primitives;` and writing `Effects.DealDamage(target, 3)` / `Effects.LoseLife(player, 3)` / etc.

This is intentionally a **facade, not a re-implementation** — the audit above shows the verbs are already implemented; consolidating the import surface is the actual leverage.

## Refactor pilots (this PR)

10 factories migrated to the new `Effects.*` facade as proof points:

- `BurstLightningFactory` — `DealDamage`
- `GalvanicDischargeFactory` — `DealDamage`
- `GutShotFactory` — `DealDamage`
- `LightningHelixFactory` — `DealAny` + `GainLife`
- `MizziumMortarsFactory` — `DealDamage`
- `ReanimateFactory` — `Zones.MoveCardToBattlefield` + `LoseLife`
- `RiftBoltFactory` — `DealDamage`
- `SearingBlazeFactory` — `DealAny`
- `TribalFlamesFactory` — `DealDamage`
- `UnholyHeatFactory` — `DealDamage`
- `DrownInTheLochFactory` — `Counter` + `MoveToGraveyard`

(11 factories — one over the 10-15 band.)

## Followups

The remaining ~277 factory files still call the underlying helpers directly. The facade exists so that future PRs can migrate them mechanically — no logic change, just an import-surface swap. Tracked under MODERN_COVERAGE.md → Mechanic infrastructure → Effects.
