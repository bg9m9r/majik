# Modern Coverage

Living tracker for Modern-format card + mechanic implementation in the Majik engine.

**Last updated:** 2026-05-23
**Latest origin/main:** 0ce5d1f (Up the Beanstalk) + Primeval Titan (this PR)

## Headline numbers

| Metric | Count |
|---|---|
| Named factories | 66 |
| Bespoke templates | 26 |
| Generic templates | 94 |
| JSON-defined cards | 15 |
| Seeded cards | 59 |
| Estimated Modern meta coverage | ~15% |

(Coverage estimate is rough: counts top-25 archetype staples present in the engine vs. total. Many ancillary archetype pieces remain unimplemented.)

## Implemented cards

### Named factories (alphabetical)

One row per file under `Majik.Core/CardData/Factories/`. PR column is the most recent PR that meaningfully touched the factory (from git log subjects).

| Card | Type | PR | Note |
|---|---|---|---|
| Agatha's Soul Cauldron | Artifact | — | activated counter-share |
| Ancient Stirrings | Sorcery | #201 | top-5 colorless reveal + random-bottom |
| Badgermole Cub | Creature | — | earthbend shell |
| Blood Moon | Enchantment | #156 | nonbasic-to-Mountain Layer 4 |
| Boseiju, Who Endures | Land | — | channel destroy stub |
| Conversion | Enchantment | #157 | Mountains-are-Plains retype |
| Consider | Instant | — | surveil 1 + draw |
| Cryptic Command | Instant | #191 | modal choose-2 |
| Dark Confidant | Creature | #178 | upkeep reveal + life loss |
| Death's Shadow | Creature | TBD | Layer 7a CDA P/T scaled by controller life |
| Delighted Halfling | Creature | — | any-color mana ability |
| Dig Through Time | Sorcery | #181 | delve top-7 to hand |
| Dredger's Insight | Enchantment | — | dies-trigger surveil-equivalent |
| Dress Down | Enchantment | #195 | lose-abilities + 1/1 base PT |
| Dryad Arbor | Land Creature | — | 1/1 Forest creature, no cost |
| Elegant Parlor | Land | — | R/W surveil dual |
| Endurance | Creature | TBD | MH2 incarnation: Flash + Reach + evoke pitch + ETB shuffle-graveyard-to-library |
| Fiery Islet | Land | — | pay-1-life U/R + sac-draw |
| Force of Negation | Instant | #185 | pitch counter (non-creature) |
| Force of Will | Instant | #185 | pitch counter (universal) |
| Fury | Creature | — | evoke pitch + ETB X-damage divided |
| Goblin Bombardment | Enchantment | — | sac-creature → 1 damage |
| Grief | Creature | #205 | evoke pitch + ETB discard |
| Grist, the Hunger Tide | Planeswalker | — | +1 token, -2 reanimate |
| Harbinger of the Seas | Creature | #157 | nonbasic-to-Island |
| Inspiring Vantage | Land | — | R/W fastland |
| Kraul Harpooner | Creature | — | fight-flyer shell |
| Lazotep Recruit | Creature | — | amass-keyword shell |
| Ledger Shredder | Creature | #193 | second-spell surveil + counter |
| Library Surveyor | Creature | — | ETB tutor shell |
| Liliana of the Veil | Planeswalker | #178 | +1 discard, -2 discard |
| Magus of the Moon | Creature | #157 | nonbasic-to-Mountain |
| Mishra's Bauble | Artifact | — | sac → look + delayed draw |
| Murktide Regent | Creature | #194 | delve cost + ETB X counters |
| Orcish Bowmasters | Creature | — | reactive ping shell |
| Pithing Needle | Artifact | #189 | name-targeted activated suppression |
| Priest of Fell Rites | Creature | #196 | ETB reanimate + grave-unearth |
| Primeval Titan | Creature | TBD | Trample + ETB/attack tutor up to 2 lands tapped |
| Rift Bolt | Sorcery | #183 | suspend → 3 damage |
| Scavenging Ooze | Creature | #188 | exile-graveyard + counter + life |
| Sea's Claim | Aura | #160 | enchanted land becomes Island |
| Snapcaster Mage | Creature | #170 | flash + ETB flashback grant |
| Solitude | Creature | — | evoke pitch + ETB exile |
| Spreading Seas | Aura | #160 | retype land + draw |
| Spymaster's Vault | Land | — | B-source shell |
| Stoneforge Mystic | Creature | #184 | ETB tutor + activated put |
| Stubborn Denial | Instant | — | ferocious-conditional counter |
| Sunbaked Canyon | Land | — | pay-1-life R/W + sac-draw |
| Surgical Extraction | Instant | #192 | phyrexian global name exile |
| Sylvan Scrying | Sorcery | TBD | any-land tutor to hand (Tron enabler) |
| Tarmogoyf | Creature | #173 | CDA P/T from grave types |
| Teferi, Time Raveler | Planeswalker | #182 | sorcery-speed restriction emblem |
| Test Conniver | Creature | — | connive-keyword test card |
| Thundering Falls | Land | — | U/R surveil dual |
| Torpor Orb | Artifact | — | ETB-trigger suppression |
| Treasure Cruise | Sorcery | #181 | delve draw 3 |
| Underground Mortuary | Land | — | U/B surveil dual |
| Unholy Heat | Instant | #190 | delirium variable damage |
| Up the Beanstalk | Enchantment | TBD | ETB draw + cast-MV-5+ draw |
| Urborg, Tomb of Yawgmoth | Land | #158 | grant Swamp to all lands |
| Vexing Bauble | Artifact | — | sac-draw shell |
| Walking Ballista | Artifact Creature | — | grow + ping |
| Wastewood Verge | Land | — | B/G activation-gate land |
| Wrenn and Six | Planeswalker | #178 | +1 land return, -1 ping |
| Wurmcoil Engine | Artifact Creature | TBD | deathtouch + lifelink + dies-trigger twin tokens |
| Yavimaya, Cradle of Growth | Land | #158 | grant Forest to all lands |
| Yawgmoth, Thran Physician | Creature | — | pay life + sac → discard/draw |

### Template-covered (notable)

Cards implemented through generic or bespoke templates without a named factory. Selected highest-value Modern staples:

- Lightning Bolt — `Damage/DamageAnyTargetTemplate`
- Lava Spike — `Damage/DamagePlayerTemplate`
- Lava Dart — `Damage/DamageAnyTargetTemplate` (flashback handled by KeywordBinder)
- Skewer the Critics — `Damage/DamagePlayerTemplate`
- Mutagenic Growth — `Counters/PumpCreatureTemplate` + Phyrexian alt cost
- Mana Tithe — `Counter/CounterUnlessPayTemplate`
- Spell Pierce — `Counter/CounterUnlessPayTemplate` (regex broadened to recognize the "noncreature" type qualifier; pay rider consults the target spell's controller's mana pool)
- Counterspell — `Counter/CounterTargetSpellTemplate`
- Boros Charm — `Bespoke/StriveTemplate`-adjacent
- Boltwave — `Damage/DealsNDamageEachOpponentTemplate`
- Goblin Guide — vanilla keyword binding (Haste + reveal-trigger TBD)
- Eidolon of the Great Revel — triggered-ability binder (cheap-spell pattern)
- Monastery Swiftspear — Prowess keyword (`Keywords/ProwessFactory`)
- Fulminator Mage — dies-destroy-land trigger
- Green Sun's Zenith — `Search/GreenSunsZenithPatternTemplate`
- Force of Vigor — `Destroy/DestroyUpToArtifactEnchantmentTemplate` + pitch cost
- Fatal Push — `Destroy/DestroyCreatureCmcLimitTemplate`
- Thoughtseize — `Bespoke/ThoughtseizePatternTemplate`
- Expressive Iteration — `Bespoke/ExpressiveIterationTemplate`
- Malevolent Rumble — `Bespoke/MalevolentRumblePatternTemplate`
- Ragavan, Nimble Pilferer — combat-damage trigger (Treasure + exile-from-top); Dash deferred
- Fetch lands cycle (10 of them) — `OracleLandActivatedAbilityBinder`
- Shock lands (Overgrown Tomb + cycle starts) — `Effects/ShockLandReplacement`
- Sacred Foundry, Steam Vents — shock-land binding
- Undying creatures (Young Wolf, Strangleroot Geist, Butcher Ghoul, Geralf's Messenger) — `Keywords/UndyingFactory`
- Ashiok, Dream Render — loyalty-ability binder (mill -1)
- Fable of the Mirror-Breaker — Saga chapter binder (partial)
- The Legend of Roku — Saga chapter binder (partial)

### JSON-defined cards

Cards under `Majik.Core/CardData/Cards/*.json`:

- boseiju.json
- delighted-halfling.json
- dredgers-insight.json
- dryad-arbor.json
- elegant-parlor.json
- inspiring-vantage.json
- lazotep-recruit.json
- library-surveyor.json
- spymasters-vault.json
- test-conniver.json
- thundering-falls.json
- underground-mortuary.json
- vexing-bauble.json
- walking-ballista.json
- wastewood-verge.json

## Mechanic infrastructure

### Costs

| Mechanic | Status | File |
|---|---|---|
| Mana cost | Done | `Costs/ManaCost.cs` |
| Mana cost reduction | Done | `Costs/CostReductionAbility.cs`, `CostReductionStaticEffect.cs` |
| Phyrexian mana | Done (#192) | `Costs/PhyrexianManaAlternativeCost.cs` |
| Flashback | Done | `Costs/FlashbackAlternativeCost.cs` |
| Runtime flashback grant | Done (#177) | `Card.RuntimeFlashbackCost` (probe in `Costs/FlashbackAlternativeCost.cs`) |
| Evoke | Done | `Costs/EvokeAlternativeCost.cs` (Solitude) |
| Delve | Done (#181) | `Costs/DelveCost.cs` |
| Pitch (exile coloured card) | Done (#185) | `Costs/PitchAlternativeCost.cs` |
| Pitch (exile any colour rider) | Done (#185) | `Costs/ExileColoredCardAlternativeCost.cs` |
| Suspend | Done (#183) | `Costs/SuspendAlternativeCost.cs`, `SuspendedCardRegistry.cs` |
| Convoke | Done (#135) | `Costs/ConvokeAlternativeCost.cs` |
| Buyback (additional) | Stub | `Costs/BuybackAdditionalCost.cs` (no caster wiring yet) |
| Madness | Stub | `Costs/MadnessAlternativeCost.cs` (no discard-replacement trigger) |
| Spectacle | Stub | `Costs/SpectacleAlternativeCost.cs` |
| Overload | Stub | `Costs/OverloadAlternativeCost.cs` |
| Cast-from-exile | Done | `Costs/CastFromExileAlternativeCost.cs` (suspend resolution) |
| Sacrifice-self | Done | `Costs/SacrificeAnotherCreatureCost.cs`, `SacrificeCreatureCost.cs`, `SacrificeBasicLandCost.cs` |
| Discard-self | Done | `Costs/DiscardSelfCost.cs` |
| Remove-counter | Done | `Costs/RemovePlusOnePlusOneCounterCost.cs` |
| Cycling | Partial | `Keywords/CyclingAbility.cs` (no enforced restriction tests; per-card hookup) |
| Echo | TODO | — |
| Kicker | TODO | — |
| Affinity | TODO | `OracleTextNormalizer` strips prefix (#136), no live cost reduction |
| Bargain | TODO | normalizer strips (#136) |

### Effects / layer system

| Mechanic | Status | File |
|---|---|---|
| CR 613 layer system (Permanent-level) | Done (#150) | `Effects/ContinuousEffectsService.cs`, `Layer.cs` |
| Dependency ordering (CR 613.8) | Done (#146) | `Effects/ContinuousEffectsService.cs` |
| Layer 1 control change | Done | `Effects/ControlChangeEffect.cs` |
| Layer 1 copy effects | Done | `Effects/CopyEffect.cs`, `EntersAsCopyReplacement.cs` |
| Layer 4 set subtypes (replace) | Done (#151) | `Effects/SetSubtypesEffect.cs` |
| Layer 4 add subtype | Done (#158) | `Effects/AddSubtypeToPermanentsEffect.cs`, `AddSubtypeEffect.cs` |
| Layer 4 aura-attached retype | Done (#160) | `Effects/AttachedAuraRetypeStaticEffect.cs` |
| Layer 4 mass land retype | Done (#157) | `Effects/RetypeLandsStaticEffect.cs` |
| Layer 4 grant land subtype | Done | `Effects/GrantLandSubtypeStaticEffect.cs` |
| Layer 6 lose all abilities (Humility / Dress Down) | Done (#149) | `Effects/LoseAllAbilitiesEffect.cs`, `DressDownStaticEffect.cs` |
| Layer 6 grant keyword (Lord-style) | Done | `Effects/LordStaticEffect.cs` |
| Layer 6 prowess pump | Done | `Effects/ProwessPumpEffect.cs` |
| Layer 7a CDA P/T (Tarmogoyf) | Done (#173) | `Effects/CdaPowerToughnessEffect.cs` |
| Layer 7b set-base P/T | Done | `Effects/BecomesPTEffect.cs` |
| Layer 7c modify P/T (auras / counters) | Done | `Effects/AttachedBoostEffect.cs`, `UntilEndOfTurnEffects.cs` |
| Layer 7d switch P/T | Done | `Effects/SwitchPTEffect.cs` |
| Combat restriction | Done | `Effects/CombatRestrictionEffect.cs`, `CombatRestriction.cs` |
| Sorcery-speed restriction (Teferi static) | Done (#182) | `Effects/SorcerySpeedRestrictionEffect.cs` |
| Name-targeted activated suppression (Pithing Needle) | Done (#189) | `Effects/PithingNeedleStaticEffect.cs` |
| ETB-trigger suppression (Torpor Orb) | Done | `Effects/TorporOrbStaticEffect.cs` |
| Effective land mana abilities derive from L4 | Done (#155) | `Effects/EffectiveManaAbilities.cs` |
| Shock-land ETB choice | Done | `Effects/ShockLandReplacement.cs` |
| Conditional ETB-tapped | Done | `Effects/ConditionalEntersTappedReplacement.cs`, `EntersTappedReplacement.cs` |
| Enters-with-counters replacement | Done | `Effects/EntersWithCountersReplacement.cs` |
| Vehicle crew | Done | `Effects/VehicleCrewEffect.cs` |
| Regeneration shield | Done | `Effects/RegenerationShieldEffect.cs` |
| Damage prevention shields | Done (#143) | `Effects/PreventAllCombatDamageShield.cs`, `PreventAllCombatDamageToPlayersShield.cs`, `PreventAllDamageToYouAndYourPermanentsShield.cs`, `PreventNextDamageFromChosenSourceShield.cs`, `PreventNextNDamageToAnyTargetShield.cs` |
| Ward keyword | Done | `Keywords/WardEffect.cs` |

### Keywords / triggers

Per-keyword action helpers under `Majik.Core/Keywords/`:

| Keyword action | Status | File | Sample card |
|---|---|---|---|
| Surveil | Done (#186) | `Keywords/SurveilAction.cs` | Ledger Shredder, surveil lands |
| Connive | Done (#186) | `Keywords/ConniveAction.cs` | Test Conniver, Spymaster's Vault stub |
| Amass | Done (#186) | `Keywords/AmassAction.cs` | Lazotep Recruit stub |
| Scry | Done | `Keywords/ScryAction.cs` | scry templates |
| Mill | Done | `Keywords/MillAction.cs` | Ashiok, mill templates |
| Cycling | Partial | `Keywords/CyclingAbility.cs` | — |
| Earthbend | Done | `Keywords/EarthbendAction.cs` | Badgermole Cub |
| Evoke | Done | `Keywords/EvokeFactory.cs` | Solitude |
| Landfall | Done | `Keywords/LandfallFactory.cs` | — |
| Prowess | Done | `Keywords/ProwessFactory.cs` | Monastery Swiftspear |
| Undying | Done | `Keywords/UndyingFactory.cs` | Young Wolf, etc. |
| Ward | Done | `Keywords/WardEffect.cs` | — |
| Convoke | Done (#135) | `Costs/ConvokeAlternativeCost.cs` + `Bespoke/ConvokeTemplate.cs` | — |
| Delirium | Done (#190) | (in `UnholyHeatFactory`; no shared helper yet) | Unholy Heat |
| Plot | TODO | — | — |
| Mobilize | TODO | — | — |
| Cascade | TODO | — | blocks Crashing Footfalls, Living End |
| Storm | TODO | — | — |
| Affinity | TODO | — | — |
| Bloodthirst / Echo / Buyback | TODO | — | — |

### Targeting / cast flow

| Surface | Status | PR / file |
|---|---|---|
| Cast-time aura targeting | Done | #171 (`Spells/`) |
| Aura "Enchant X" parser | Done | #176 (`CardData/Parsing/`) |
| Sorcery-speed restriction (Teferi) | Done | #182 (`Effects/SorcerySpeedRestrictionEffect.cs`) |
| Name-targeted activated suppression (Pithing Needle) | Done | #189 (`Effects/PithingNeedleStaticEffect.cs`) |
| Modal choose-N spells | Done | #191 (Cryptic Command) |
| Strive cost-per-extra-target | Done | #134 (`Bespoke/StriveTemplate.cs`) |
| Spell-copy primitive | Done | #129 (`Bespoke/NextSpellCopyTemplate.cs`) |
| Redirect (Deflection / Swerve) | Done | #133 (`Bespoke/RedirectTemplate.cs`) |
| Reveal-until-condition family | Done | #126 (`Bespoke/RevealUntil*.cs`) |
| Reveal-N → battlefield family | Done | #139 (`Bespoke/RevealN*.cs`) |
| Put-target-second-from-top | Done | #123 (`Misc/PutTargetSecondFromTopTemplate.cs`) |
| Pump-then-return | Done | #120 (`Bespoke/PumpThenReturnTemplate.cs`) |
| Same-name pump (Coat of Arms-style) | Done | #106 (`Counters/SameNamePumpTemplate.cs`) |
| Var-pump-per-creature | Done | #105 (`Counters/VarPumpPerCreatureTemplate.cs`) |
| Support template | Done | #121 (`Counters/SupportTemplate.cs`) |
| Wheel template | Done | #113 (`Library/WheelTemplate.cs`) |

### Zone moves / ETB triggers

| Surface | Status | PR |
|---|---|---|
| Cast-resolution ETB triggers | Done | pre-existing (`Services/ZoneService.cs`) |
| Reanimation ETB triggers | Done | #165 |
| Mass-reanimation ETB triggers | Done | #174 |
| Compiled-template rehydrate threads ZoneService | Done | #175 |
| Hand-reveal events | Done | #164 |
| Per-viewer event masking (CR 706) | Done | #168, #169 |
| Delayed triggered abilities | Done | `Abilities/` (used by Mishra's Bauble) |

## Coverage by archetype

- **Burn** — Strong. Lightning Bolt, Lava Spike, Lava Dart, Skewer the Critics, Boros Charm, Eidolon of the Great Revel, Goblin Guide, Monastery Swiftspear, Rift Bolt all in. Missing: Searing Blaze (landfall conditional), Roiling Vortex, Sunscorched Desert. ~75%.
- **Death's Shadow** — Mid-high. Thoughtseize, Fatal Push, Snapcaster Mage, Stubborn Denial, Death's Shadow itself (CDA P/T scaled by controller life — Layer 7a) all in. Mishra's Bauble in. Temur Battle Rage absent. ~60%.
- **Murktide / Izzet Tempo** — High. Murktide Regent done, Counterspell done, Snapcaster Mage done, Lightning Bolt done, Expressive Iteration done, Ledger Shredder done, Consider done, Spell Pierce done. Missing: Unholy Heat is done but Demilich/Subtlety absent. ~70%.
- **Mono-Green Tron** — Mid-low. Ancient Stirrings, Sylvan Scrying, Wurmcoil Engine done. No Karn Liberated, no Tron lands. ~20%.
- **Living End / Crashing Footfalls cascade** — Blocked. Cascade keyword + Suspend-trigger end-of-suspend exile-and-cast TODO. Suspend itself is done (#183), so partial groundwork. ~15%.
- **Rakdos Scam** — Mid. Grief done (#205, mirrors Solitude evoke + ETB pattern). Dauthi Voidwalker absent. Fury absent. Liliana of the Veil done, Fatal Push done, Thoughtseize done. ~40%.
- **Yawgmoth combo** — Mid. Yawgmoth done. Undying creatures (Young Wolf, Strangleroot Geist, Geralf's Messenger) done. Chord of Calling, Eldritch Evolution absent. ~50%.
- **Domain Zoo** — Low-mid. Boros Charm done, fetches done, shocks done. Scion of Draco, Territorial Kavu, Tribal Flames absent. ~25%.
- **Amulet Titan** — Low. Primeval Titan done (ETB + attack land-tutor for up to 2, tapped). No Amulet of Vigor, no bounce lands. ~15%.
- **Hammer Time / Equipment** — Low-mid. Stoneforge Mystic done. Sigarda's Aid, Colossus Hammer, Puresteel Paladin absent. ~20%.

## Top 20 Modern staples NOT yet implemented

Sorted by build priority (small infra lift × high meta share).

| # | Card | Difficulty | Blocker |
|---|---|---|---|
| 1 | Karn, the Great Creator | Mid | Sideboard-from-anywhere -2 ability needs wishboard concept |
| 2 | Karn Liberated | Mid | Exile target, restart-game ultimate (game-restart deferred) |
| 3 | Urza's Tron pieces (Mine/Tower/Power Plant) | Mid | "Tap: add 1; if you control all three, add 3" — conditional mana ability |
| 4 | Subtlety | Low | Evoke + ETB bounce-and-look — Solitude/Grief/Fury pattern + bounce template |
| 5 | Crashing Footfalls | High | Suspend done (#183), but cascade trigger on suspend-cast missing |
| 6 | Living End | High | Cascade + mass-exile-grave + simultaneous mass-reanimate (#174 ready for the latter) |
| 7 | Cascade keyword | High | Triggered "cast for free from top reveal" — alt-cast-from-library framework |
| 8 | Amulet of Vigor | Mid | Replacement on enters-tapped → untap; needs ETB replacement composition |

## How to update this doc

After merging a card-shipping PR:
1. Append the new card to the **Named factories** table (or **Template-covered (notable)** if it's a template port).
2. Remove it from **Top 20 not yet implemented** if it appears there.
3. Bump the **Last updated** date and **Latest origin/main** hash at the top.

After merging a mechanic-infra PR:
1. Flip the relevant row in **Costs**, **Effects**, **Keywords**, or **Targeting / cast flow** to **Done (#PR)**.
2. If a top-20 entry's blocker is resolved, drop its difficulty one tier in the priority table.

Keep this file terse. New cards = one row. New mechanics = one row. Long-form rationale lives in PR descriptions, not here.
