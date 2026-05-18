# Majik Engine Roadmap — Rules-Accurate Magic Simulator

Living document. Updated as phases complete.

## Current State (after Phase 13)

572 tests, zero warnings. Engine plays a slice of Magic end-to-end:

- Game/Turn/Phase loop with priority rounds (CR 117, 5xx)
- Stack + resolution (CR 405, 608)
- Triggered abilities (CR 603) — full APNAP, intervening-if, delayed, state-change
- State-based actions framework (CR 704) — life, creature/planeswalker death, legend rule
- Zones (CR 4xx) + ZoneService
- Async `IPlayerAgent` for every choice point
- Mana pool + cost validation (CR 106, 202)
- Basic combat (declare attackers/blockers/damage; tap; vigilance honored)
- Card data: Scryfall DB + `KeywordBinder` + `OracleManaBinder` + `OracleSpellBinder`
- DTO/Web API surface (`Majik.Core.Api`)

## Gap Analysis vs Comprehensive Rules

| Rules Area | CR Section | Engine Status |
|---|---|---|
| Game concepts | 1xx | ✅ partial |
| Parts of a card | 2xx | ⚠️ types/cost/PT — no loyalty abilities, no oracle text full parse |
| Card types | 3xx | ⚠️ basic types only; no Battles, Sagas, Adventures, MDFCs, Class |
| Zones | 4xx | ✅ |
| Turn structure | 5xx | ✅ |
| Spells/abilities | 6xx | ⚠️ casting partial; activation missing; modal/X partial |
| Combat | 5xx + 7xx | ⚠️ no first/double strike split, no damage assignment ordering, no trample/deathtouch/lifelink |
| Effects | 6xx | ❌ no replacement, no prevention, no continuous (layer system) |
| Keywords | 702 | ⚠️ 13 markers; no functional implementations |
| State-based actions | 704 | ✅ subset (5 of 14 checks) |
| Targeting | 115 | ⚠️ no legality validation per-card |
| Counters | 122 | ❌ none |
| Tokens | 111 | ❌ none |
| Copies | 707 | ❌ none |
| Multiplayer | 8xx | ❌ none (1v1 only) |
| Casting cost variants | 117/118 | ❌ no additional/alternative costs |

---

## Phase Catalogue

### Phase 14 — Combat Completeness (~2 weeks) **← DONE**

**Goal**: real Magic combat per rules 506-511, 702 (combat keywords).

- Damage steps: first-strike step + regular damage step (CR 510.2/.3)
- Damage assignment order for multiple blockers (CR 509.2)
- Trample (702.19) — overflow to defender
- Deathtouch (702.2) — any damage from deathtouch source is lethal
- Lifelink (702.15) — damage source's controller gains life
- Double strike (702.4)
- First strike (702.7) — damage step ordering
- Indestructible (702.12) — damage doesn't destroy
- Protection (702.16) — DEBT (damage/enchant/block/target)
- Reach (702.17), Flying (702.9), Menace (702.110), Defender (702.3) — block legality
- Combat damage triggers ("whenever ~ deals combat damage")
- Planeswalker as attack target + combat damage routing

**Files**: extend `Combat/CombatFlow.cs`; split damage assignment; introduce `DamageEvent` + `CombatDamageDealtEvent`.

### Phase 15 — Casting Completeness (~2 weeks)

**Goal**: every spell can be cast per CR 601.

- Casting permissions: sorcery speed vs instant speed (CR 117.1)
- Land drop tracking + restriction (CR 305.2 — once per turn, main phase, stack empty)
- Hold priority after casting (already partial)
- Ability activation flow (CR 602) — symmetric with SpellCastFlow
- Additional costs ("as you cast", CR 601.2f)
- Alternative costs (Flashback, Madness, Cipher, Cast from Exile)
- Variable costs (X spells) — wire payment into ManaPaymentResolver
- Modal spells (CR 700.2) — single/multi-mode prompts
- Targets legality validation against current game state (CR 601.2c, 608.2b)
- Illegal-target → counter on resolve (CR 608.2b)
- Wire `Counterspell` into real cast flow (today only direct effect call)
- Reveal/show stack — visible to all players (CR 401.5)

### Phase 16 — Continuous Effects + Layer System (~3 weeks)

**Goal**: CR 613 layer system. Required for ANY "creatures get +1/+1", "becomes a 4/4", static abilities granting flying, etc.

7 layers:
1. Copy effects
2. Control-changing
3. Text-changing
4. Type-changing
5. Color-changing
6. Ability-adding/removing
7. P/T (7a CDA, 7b set, 7c modify, 7d switch)

Plus characteristic-defining abilities, timestamp/dependency ordering.

### Phase 17 — Replacement + Prevention Effects (~2 weeks) **← DONE**

CR 614 (replacement) + CR 615 (prevention).

- `Effects/IReplacementEffect.cs` — intercepts events before they happen
- Replacement effect registry — bound to permanent/static ability
- Self-replacement effects (apply once)
- "Enters tapped" replacement
- "Enters with N counters" replacement
- Prevention shields: "prevent the next N damage" + decrement

### Phase 18 — Counters + Tokens + Copies (~1.5 weeks)

CR 122, 111, 707.

- `Counters/CounterType.cs` enum + `CounterCollection.cs` value object on Permanent
- +1/+1 and -1/-1 cancel each other (704.5q)
- Loyalty counters (planeswalker activations cost loyalty)
- `Tokens/TokenFactory.cs` — create token from template; tokens cease to exist outside battlefield (704.5d)
- Copy effects (Clone, copy spell on stack) — apply as Layer 1

### Phase 19 — Card Type Subsystems (~3 weeks)

CR 3xx — type-specific gameplay.

- Auras (CR 303.4) — attachment, "enchanted creature", bestow
- Equipment (CR 301.5) — equip activated ability, attach/unattach
- Vehicles (CR 301.7) — Crew, becomes creature
- Planeswalker loyalty abilities (CR 606)
- Sagas (CR 714) — chapter triggers, sacrifice on final
- Adventures (CR 715) — exile + later cast
- MDFCs / Transform (CR 711) — back face
- Battles (CR 310) — defense counters, defender choice
- Class (CR 716) — level activated abilities
- Mutate (CR 702.139)

### Phase 20 — Full Keyword Library (~3 weeks)

CR 702 — all 200+ evergreen + deciduous + non-evergreen keywords.

- Generate stub binder per keyword from `KeywordRegistry`
- Implement runtime effect for each
- Parameterized keywords: Ward {X}, Protection from Y, Cycling {X}, Kicker {X}
- Activated keyword abilities: Equip {X}, Crew {X}, Mutate {X}, Outlast
- Triggered keyword abilities: Landfall, Constellation, Magecraft, Prowess
- Static keyword abilities migrate from KeywordAbility markers to ContinuousEffect

### Phase 21 — Oracle Text Parser Completeness (~3 weeks)

Extend binders to cover ~80%+ of real cards. Grammar over text instead of regex pile.

### Phase 22 — Targeting + Legality + Choice Validation (~1.5 weeks)

CR 115 / 601.2c / 608.2b.

- `Targeting/TargetSpec.cs` — full predicate (type, color, controller, P/T, name)
- `Targeting/TargetLegality.cs` — given spec + game state, enumerate legal targets
- Validation at cast time + resolution
- Hexproof, Shroud, Protection block targeting

### Phase 23 — Multiplayer + Formats (~2 weeks)

CR 8xx multiplayer, CR 9xx variants.

- N players (3-6) — generalize current 1v1 priority loop
- Range of influence (CR 801)
- Commander (CR 903): command zone, commander tax, commander damage, color identity check
- Brawl, Standard, Modern format legality from `Legalities` JSON

### Phase 24 — RNG + Hidden Information (~1 week)

CR 100 (shuffling), CR 706 (hidden info).

- True PRNG seeded per game (seedable for replay)
- Shuffle library on game start + after search effects (CR 701.20)
- Coin flip, dice roll
- Per-player `GameStateDto` masks opponent hand / library

### Phase 25 — Mulligan + Game Start (~1 week)

CR 103, 104.

- Wire `MulliganController` into `GameDriver`
- Determine starting player
- London mulligan loop per player
- "As the game begins" effects

### Phase 26 — State-Based Actions Completeness (~1 week) **← MVP DONE**

Remaining 9 of 14 checks. Empty-library loss, poison, illegal attach, etc.

### Phase 27 — Bot Intelligence (~2 weeks first cut)

Smarter `IPlayerAgent` — `HeuristicBotAgent`, threat assessment, combat plan, pluggable difficulty.

### Phase 28 — Web Frontend (~3 weeks, out-of-scope-but-listed)

ASP.NET Core wrapper + separate-repo React/Svelte frontend.

### Phase 29 — Persistence + Replay (~1 week)

`GameSnapshot` entity + action log → deterministic replay.

### Phase 30 — Performance + Hardening (~2 weeks)

Card lookup cache, frozen ability bindings, concurrent-game stress test, telemetry.

---

## Recommended Execution Tracks

**Track A (rules correctness)** — sequential: 14 → 17 → 16 → 18 → 26 → 22
**Track B (card coverage)** — parallel-feasible: 20 → 21 → 19 → 25
**Track C (production)** — final: 23 → 24 → 29 → 30 → 27 → 28

Track A unlocks Track B (layer system needed for granted abilities).

## Acceptance Criteria for "Rules-Accurate"

1. ≥ 95% of cards in a chosen format auto-bind via oracle parser
2. Comprehensive test suite covering every CR section relevant to gameplay
3. Replay determinism: snapshot + log replays byte-identically
4. 4-player Commander game completes without errors
5. 100 concurrent games on commodity hardware, < 100ms per command
6. Per-card behavior verified against Scryfall ruling text for curated 100-card sample

---

## Timeline

| Phase | Theme | Est | Cumulative |
|---|---|---|---|
| 14 | Combat completeness | 2w | 2w |
| 15 | Casting completeness | 2w | 4w |
| 16 | Layer system | 3w | 7w |
| 17 | Replacement/prevention | 2w | 9w |
| 18 | Counters/tokens/copies | 1.5w | 10.5w |
| 19 | Card type subsystems | 3w | 13.5w |
| 20 | Keyword library | 3w | 16.5w |
| 21 | Oracle parser | 3w | 19.5w |
| 22 | Targeting | 1.5w | 21w |
| 23 | Multiplayer + formats | 2w | 23w |
| 24 | RNG + hidden info | 1w | 24w |
| 25 | Mulligan / game start | 1w | 25w |
| 26 | SBA completeness | 1w | 26w |
| 27 | Bot intelligence | 2w | 28w |
| 28 | Web frontend | 3w | 31w |
| 29 | Persistence/replay | 1w | 32w |
| 30 | Perf + hardening | 2w | 34w |

~8 months solo full-time. Critical path through Phase 21 (oracle parser).

---

## Phase Status Log

- **Phase 8** — Choice + async game loop ✅ done
- **Phase 9** — DTOs, GameFacade, GameRegistry, snapshot save/load ✅ done
- **Phase 10** — Card factory, ability library (initial), combat wiring, mana auto-pay, mulligan controller ✅ done
- **Phase 10.5** — DB-backed CardFactory ✅ done
- **Phase 11** — TurnDriver + GameDriver ✅ done
- **Phase 12** — Hand-coded ability expansion → **superseded** by Phase 13
- **Phase 13** — Data-driven binders (KeywordBinder, OracleManaBinder, OracleSpellBinder) + AbilityLibrary deleted ✅ done
- **Phase 14** — Combat completeness ✅ done
  - First/double strike damage steps (CR 510.2/.3)
  - Deathtouch (CR 702.2): any nonzero damage from deathtouch source = lethal (via `MarkedForDestructionByDeathtouch` flag + SBA)
  - Lifelink (CR 702.15): controller gains life equal to damage dealt
  - Trample (CR 702.19): overflow to defender
  - Indestructible (CR 702.12): SBA skip
  - Multi-blocker damage assignment per CR 510.1c (lethal-each or skip; no leftover dump)
  - Block legality: Flying/Reach (CR 509.1b), Defender (CR 508.1a), Menace (CR 702.110), summoning sickness vs Haste
  - Planeswalker as attack target — damage removes loyalty (CR 120.3c); 0 loyalty → graveyard via SBA
  - `CombatDamageDealtEvent` published per damage instance
  - 563 Core + 31 Api = 594 tests, zero warnings
- **Phase 17** — Replacement + Prevention effects ✅ done
  - `IReplacementEffect<TIntent>` + `LambdaReplacement<TIntent>` (CR 614)
  - `ReplacementBus` — each effect fires at most once per intent (CR 616.1c), cancellation supported, one-shot effects auto-unregister
  - `DamageIntent` — every combat-damage path funnels through bus before applying; "prevent next N damage" shield demoed (CR 615.1)
  - `ZoneMoveIntent` — every zone change funnels through bus; "enters tapped" demoed; "if would die, exile instead" demoed
  - `CombatFlow` + `ZoneService` now take optional `ReplacementBus`
  - 575 Core + 31 Api = 606 tests, zero warnings
- **Phase 16** — Continuous effects + Layer system (MVP) ✅ done
  - `Layer` enum covering all 7 CR 613 layers (sub-7a/b/c/d). MVP impl: Layer 6 (Abilities) + Layer 7c (PT_Modify); other layers wireable
  - `ContinuousEffect` abstract — `Layer`/`AppliesTo`/`Apply`/`IsActive`/`ExpiresAtEndOfTurn`/`Timestamp`
  - `CreatureCharacteristics` working-set — Power, Toughness, Keywords (modified during layer pass)
  - `ContinuousEffectsService` — registry; `Compute(creature)` runs effects in layer + timestamp order; `ExpireEndOfTurn()` for CR 514.2 cleanup
  - `Creature.ActiveEffects` opt-in — when set, `Power`/`Toughness` flow through service
  - `CombatAbilities.Has*` flow through service when wired (granted keywords detected)
  - `TurnDriver.Cleanup` calls `service.ExpireEndOfTurn()` at end-of-turn
  - Demos: "Giant Growth"-style pump expires at end-of-turn; "Glorious Anthem"-style static +1/+1 only while source on battlefield, only affects controller's creatures
  - 588 Core + 31 Api = 619 tests, zero warnings
- **Phase 18** — Counters + Tokens + Copies ✅ done
  - `Counters/CounterType` (record) + `Counters/CounterCollection` (add/remove/count by type)
  - `Permanent.Counters` attached to every permanent; `Permanent.IsToken` flag
  - +1/+1 and -1/-1 counter P/T baked into `ContinuousEffectsService.Compute` after Layer 7c effects (CR 122.1g)
  - SBA 704.5q: +1/+1 vs -1/-1 paired cancellation
  - SBA 704.5d: tokens off battlefield cease to exist
  - `Tokens/TokenFactory.CreateOnBattlefield` builds keyword-aware token creatures, routes through ZoneService so triggers (Soul Warden etc.) fire
  - `Effects/CopyEffect` — Layer 1; copier takes original's P/T + printed keywords; later layers (modify, counters) still apply on top
  - 603 Core + 31 Api = 634 tests, zero warnings
- **Phase 19a** — Auras + Equipment subset of Card Type Subsystems ✅ done
  - `Permanent.AttachedTo` + `Attachments` bidirectional refs; `AttachTo`/`Unattach` API
  - SBA: Aura without legal attachment → graveyard (CR 704.5n); Equipment whose bearer left → unattaches but stays (CR 704.5h)
  - `Effects/AttachedBoostEffect` — Layer 7c generic boost (+P/+T) + Layer 6 keyword grant; reads `source.AttachedTo` dynamically (re-equipping transfers bonus)
  - Demos: Holy Strength aura grants +1/+2; Sword of Strength equipment grants +2/+2 + First strike; equipment transfer between bears mid-game
  - 615 Core + 31 Api = 646 tests, zero warnings
- **Phase 19 remaining** — Vehicles (Crew), Sagas (chapter triggers), Adventures, MDFCs/Transform, Battles, Class, Mutate, Planeswalker activated loyalty abilities (mechanics, not the type itself)
- **Phase 22** — Targeting + Legality ✅ done
  - `Targeting/TargetSpec` — declarative spec: type filters (creature/player/planeswalker/artifact/enchantment/land), controller filters, additional predicate
  - `Targeting/TargetLegality` — single-candidate `IsLegal()` + `EnumerateLegal()` walker over battlefield + players
  - Hexproof (own-controller exception), Shroud (all-blocking), Protection from COLOR — keyword-aware target filtering
  - Resolution-time recheck pattern (CR 608.2b) demonstrated via tests — full SpellCastFlow integration deferred to Phase 15
  - 625 Core + 31 Api = 656 tests, zero warnings
- **Phase 26** — State-Based Actions completeness (MVP) ✅ done
  - `Player.TriedToDrawFromEmptyLibrary` sticky flag — CR 704.5b lose condition
  - `Player.PoisonCounters` + 10+ poison loss — CR 704.5c
  - Unified `CheckPlayerLife` SBA pass handles all three loss conditions (life≤0, deck-out, poison)
  - 628 Core + 31 Api = 659 tests, zero warnings
- **Phase 15, 20, 24-30** — pending; layer system deferments: Layer 2 (control), 3 (text), 4 (type), 5 (color), 7a (CDA), 7b (set base), 7d (switch); CR 613.8 dependency ordering; copies of spells on the stack
- **Phase 26 remaining** — Battle 0-defense check (CR 704.5n) requires Battle card type from Phase 19 remainder; "spell on stack with no card" (CR 704.5e); planeswalker uniqueness already done but old (rule removed 2022)
- **Phase 20 first cut** — Keyword runtime impls (5 keywords) ✅ done
  - Cycling (CR 702.31) — `CyclingAbility` self-discard + draw; flags empty-library on draw attempt
  - Scry N (CR 701.20) — `ScryAction.Peek`/`Apply` reorders library top per agent decision
  - Ward N (CR 702.21) — `WardEffect` counters opponent's spell unless cost paid
  - Prowess (CR 702.108) — `ProwessFactory` builds triggered ability + Layer 7c pump-until-end-of-turn
  - Landfall (CR 702.140) — `LandfallFactory` parameterised triggered ability on land entering under your control
  - 643 Core + 31 Api = 674 tests, zero warnings
- **Phase 20 remaining** — ~195 keywords still ad-hoc; needs catalog pass against `KeywordRegistry` to track coverage
