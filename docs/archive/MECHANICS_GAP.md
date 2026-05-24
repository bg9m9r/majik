# Mechanics Gap Audit

Snapshot of what the Majik engine **does** vs **doesn't** support, grouped
and prioritised. Bot vs. bot test runs are only meaningful once the
high-impact bands here are covered — see the `play-modern-faceoff` log
where Eldrazi-Affinity casts ~0 creatures because Affinity / Metalcraft /
Saga chapters are all stubbed.

Implemented checks below reflect: **F**ramework (plumbing exists), **B**inder
(oracle parser wires it to real cards), **W**ired (used by engine paths).
A line missing all three = essentially absent.

---

## P0 — blocks Modern/Standard-style decks from playing meaningfully

| Mechanic | F | B | W | Notes |
| --- | --- | --- | --- | --- |
| Shock-land "ETB tapped unless 2 life" | – | – | – | Steam Vents / Sacred Foundry / Breeding Pool — bot can never use them as colour sources untapped on the play turn. |
| Fetch lands ("Tap, pay 1 life, sac, search") | – | – | – | Polluted Delta, Marsh Flats, Flooded Strand, Arid Mesa, Scalding Tarn — currently inert; no mana ability, no fetch effect. |
| `Add one mana of any color` (Mox Opal, City of Brass) | – | – | – | OracleManaBinder regex only matches `{T}: Add {COLOUR}.`; modal "any color" missed. |
| Metalcraft / "if you control three or more artifacts" gates | – | – | – | Mox Opal's mana ability is gated; without metalcraft, no mana. |
| Cost reduction (Affinity-for-X, Improvise, Convoke, Delve) | partial | – | – | `CostReductionStaticEffect` framework exists; no binder, no SpellCastFlow integration. |
| Energy counters (`{E}` symbol, spend/gain energy) | – | – | – | Galvanic Discharge, Guide of Souls inert. Counter type not modelled. |
| Urza's Saga chapter triggers (Construct tokens, tutor) | partial | – | – | `SagaState` advances + sacrifices; chapter callbacks aren't wired by any binder. |
| Modal DFC (Shatterskull Smashing, Birgi // Harnfel) | – | – | – | DB has the "Front // Back" name; no flip / cast-as-front mechanism. |
| Treasure tokens (Ragavan, etc.) | – | – | – | Ragavan deals damage to player — should create Treasure; not bound. |
| `{X}` cost effects with real X (Engineered Explosives, Demonfire) | partial | partial | – | `HasVariableX` flag + `ChooseXAsync` exist; bot always picks 0; sweep/charge templates incomplete. |
| Cantrips ("Draw a card" effect generally) | – | – | – | Mishra's Bauble, Preordain, etc. — no binder template wires the draw effect onto the cast resolution. |
| Cascade | – | – | – | Living End-style entire archetype dead. |

## P1 — high-impact missing keyword evergreens / common abilities

| Mechanic | F | B | W | Notes |
| --- | --- | --- | --- | --- |
| Ward (N) / Ward — pay life / Ward — sac | – | – | – | Common modern keyword; protects from targeting. |
| Prowess | – | – | – | Whenever you cast a non-creature spell, this gets +1/+1 EOT. |
| Landfall | – | – | – | Triggers on land ETB. |
| Affinity (as a keyword, distinct from the cost-reduction we noted above) | – | – | – | "This spell costs {1} less per X you control." |
| Madness | – | – | – | Cast from exile when discarded. |
| Flashback | partial | – | – | `OverloadAlternativeCost` shape exists; Flashback specifically not bound from oracle text. |
| Buyback | partial | – | – | `BuybackAdditionalCost` exists; binder hookup absent. |
| Kicker / multikicker | – | – | – | Cost addition + on-resolution effect change. |
| Cycling | – | – | – | Activated ability from hand: discard + draw. |
| Morph / Megamorph / Disguise | – | – | – | Cast face-down for {3}, flip later. |
| Dredge | – | – | – | Replace draw with mill + return. |
| Threshold / Delirium / Spell-mastery | – | – | – | State-count gates on graveyard composition. |
| Goad / Provoke | – | – | – | Forced-attack / forced-block restrictions. |
| Equip / Reconfigure / Living weapon | partial | – | – | `AttachTo` exists on Permanent; activated `{X}: equip` not wired. |
| Mutate | – | – | – | Stack creatures into one. |
| Regenerate (`Regen target X`) | partial | – | – | `RegenerationShieldEffect` exists; binder template doesn't emit it. |
| Indestructible (granted, not printed) | partial | partial | – | Printed Indestructible works in SBA; "gains indestructible until end of turn" not bound. |
| Protection — from creatures / from non-colour qualities | partial | – | – | `Protection.HasProtectionFromCardType` exists; not enforced in combat/targeting yet (only colour is). |
| Hexproof from colour ("Hexproof from red") | – | – | – | Subtype of hexproof; not modelled. |
| Counter target spell **unless** controller pays X | partial | partial | – | Binder matches the regex but the "unless pay" prompt isn't wired (currently just counters). |
| Ascend / city's blessing (CR 702.131) | – | – | – | "Once you control 10+ permanents, you have the city's blessing for the rest of the game." No `Player.HasCitysBlessing` latch yet. First card touching this gate: **Ocelot Pride** (MH3) — its attack trigger ships with the gate stubbed (always 1 Cat token; the "if you have the city's blessing, instead create two" half is deferred). Future blockers: any other Ascend card (Pride Sovereign, Slaughter the Strong, Etrata-adjacent printings). |

## P2 — additional cost / alternative cost surface

| Mechanic | F | B | W | Notes |
| --- | --- | --- | --- | --- |
| Phyrexian payment with 2 life | partial parse | – | – | Pip parses; payment path doesn't accept life. |
| Hybrid pip payment ({R/G}) | partial parse | – | – | Same. |
| `{2/W}` two-or-colour hybrid payment | partial parse | – | – | Same. |
| Snow mana (`{S}`) | – | – | – | Symbol not parsed; snow lands inert as snow sources. |
| Improvise / Convoke / Delve as cost reducers | – | – | – | Tap artifacts / creatures / exile cards as part of payment. |
| Suspend / Foretell / Adventure / Disturb / Eternalize / Embalm | – | – | – | Cast-from-non-hand variants. |
| Splice onto Arcane | – | – | – | Card piggy-backed onto another spell at cast. |

## P3 — broader engine / rules gaps

| Mechanic | F | B | W | Notes |
| --- | --- | --- | --- | --- |
| Damage prevention shields (separate from Regeneration) | – | – | – | "Prevent the next N damage…" |
| Damage redirection (planeswalker redirect was removed in 2018; still other redirects) | – | – | – | "All damage from X is dealt to Y instead." |
| Day / Night | – | – | – | Daybound / Nightbound mechanic + flip. |
| The Monarch / The Initiative | – | – | – | Multiplayer designations. |
| Poison ≥ 10 SBA loss | partial | – | – | Player.PoisonCounters exists; Infect / Toxic / Proliferate not bound. |
| Energy counters as a player-level resource | – | – | – | Separate from card counters. |
| Experience / Cohort / Storm / Tempo counters | – | – | – | Various player-scoped trackers. |
| Companion (deckbuilding constraint + cast-from-sideboard cost) | – | – | – | Sideboard zone partially considered in DeckValidator. |
| Sideboarding between games | – | – | – | Game series not modelled. |
| Surveil / Scry choice (look-then-bottom) | partial | partial | – | Scry binds as no-op; needs agent prompt. |
| Library shuffle after search | – | – | – | `IZone.Shuffle` not exposed; tutor templates note this. |
| Token "scaling" rules (e.g. Construct power = artifact count) | – | – | – | Tokens spawn vanilla; on-the-fly P/T expressions missing. |

## P4 — combat polish

| Mechanic | F | B | W | Notes |
| --- | --- | --- | --- | --- |
| Damage assignment ordering by defender (multi-block) | partial | – | – | First/double-strike handled; the player-choice of which blocker takes which damage isn't surfaced. |
| Restrictions (cannot attack) — non-Ghostly-Prison varieties | partial | – | – | `AttackRestriction` base exists; only PayPerAttacker shipped. |
| Requirements (must attack / must block) | – | – | – | E.g. Goad. |
| Combat-damage triggers (Ragavan: pillage trigger on hit) | partial | – | – | `CombatDamageDealtEvent` fires; TriggerManager dispatches; no binder catches the common "whenever ~ deals combat damage to a player" patterns into Treasure / pillage / draw. |
| Banding | – | – | – | Legacy mechanic, rare; low priority. |

## P5 — agent / UX gaps

| Mechanic | F | B | W | Notes |
| --- | --- | --- | --- | --- |
| Smart instant-speed casting on opponent's turn | – | – | – | HeuristicBot only acts at sorcery speed today. |
| Mana-cost-aware sequencing (leave up `{1}{U}` for Counterspell) | – | – | – | Bot dumps mana on the biggest threat each turn. |
| Mulligan: keep based on actual castability curve | partial | – | – | Bot keeps on 2–5 lands; doesn't simulate spells castable. |
| Target selection by threat assessment | – | – | – | Bot picks first legal candidate; should prefer opposing planeswalker / biggest creature / lowest-life player. |
| Trigger ordering by impact (APNAP within controller-group) | – | – | – | Bot returns triggers in given order. |
| Block: chain blocks (multiple blockers vs one big attacker) | – | – | – | One-per-attacker only. |
| Activated-ability use (mana abilities done; non-mana activations like Goblin Bombardment sac) | – | – | – | Bot never activates printed activated abilities. |
| Sideboarding agent decisions | – | – | – | N/A until matches exist. |

---

## Priority recommendation for the next few weeks

Single biggest unlock for "decks actually play their gameplan":

1. **Shock-land + fetch-land mana fix** (one cut). Bot's mana base becomes
   real; Bob can cast actual spells. Both lands are present in nearly every
   modern deck — without them everything else compounds on a broken base.
2. **`Add one mana of any color`** binder pattern + Mox Opal metalcraft.
   Affinity decks actually generate mana.
3. **Cantrip / "draw a card" binder template**. Mishra's Bauble, Preordain,
   Consider, etc. all light up; bot hand quality improves.
4. **Energy counters + spend `{E}` cost**. Boros Energy stops bypassing
   half its rules.
5. **Treasure tokens**. Ragavan does its real job; ramp decks function.
6. **Cost-reduction wiring** (Affinity-for-artifacts / Convoke / Improvise).
   Most modern decks use one of these.
7. **Urza's Saga chapters** (token generation + tutor). Land that's also a
   threat — without it Affinity loses tempo.
8. **Counter-unless-pay prompt**. Mana Leak / Metallic Rebuke / Force Spike
   actually function as counters.
9. **Ward** evergreen — common modern protection.
10. **Equipment activated `{X}: equip`** + the resulting attached-creature
    static boost.

Each of these is a discrete cut — most are within a few hours of work each.
After P0 + the P1 quick wins, bot vs. bot results start reflecting deck
strategy instead of binder coverage.
