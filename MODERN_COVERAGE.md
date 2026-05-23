# Modern Coverage

Living tracker for Modern-format card + mechanic implementation in the Majik engine.

**Last updated:** 2026-05-23
**Latest origin/main:** 6b29e01 (… + Lurrus of the Dream-Den + Colossus Hammer + Sigarda's Aid + Living End + Phyrexian Tower + Puresteel Paladin + Aether Vial + Mox Opal + Scion of Draco + Ragavan, Nimble Pilferer + Cavern of Souls + Galvanic Discharge + Dauthi Voidwalker + Chord of Calling) + Eldritch Evolution (this PR)

## Headline numbers

| Metric | Count |
|---|---|
| Named factories | 89 |
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
| Aether Vial | Artifact | TBD | upkeep charge counter + {T} put creature from hand with mv = counters |
| Agatha's Soul Cauldron | Artifact | — | activated counter-share |
| Amulet of Vigor | Artifact | TBD | untap-on-enters-tapped trigger |
| Ancient Stirrings | Sorcery | #201 | top-5 colorless reveal + random-bottom |
| Badgermole Cub | Creature | — | earthbend shell |
| Blood Moon | Enchantment | #156 | nonbasic-to-Mountain Layer 4 |
| Boseiju, Who Endures | Land | — | channel destroy stub |
| Cavern of Souls | Land | TBD | ETB choose-creature-type + {T}: {C} + {T}: any color (spend-restriction + uncounterable rider deferred) |
| Chord of Calling | Instant | TBD | Flash + Convoke + X tutor creature mv ≤ X → battlefield (convoke reduction integration deferred) |
| Colossus Hammer | Artifact | TBD | Equipment {1}: +10/+0 + lose flying + equip {8} |
| Conversion | Enchantment | #157 | Mountains-are-Plains retype |
| Consider | Instant | — | surveil 1 + draw |
| Crashing Footfalls | Sorcery | TBD | cascade trigger + 2x 4/4 Rhino warrior tokens with trample |
| Cryptic Command | Instant | #191 | modal choose-2 |
| Dark Confidant | Creature | #178 | upkeep reveal + life loss |
| Dauthi Voidwalker | Creature | TBD | Shadow + opponent-grave→exile-with-void-counter replacement + {2},{T},remove counter: cast-for-free from exile |
| Death's Shadow | Creature | TBD | Layer 7a CDA P/T scaled by controller life |
| Delighted Halfling | Creature | — | any-color mana ability |
| Dig Through Time | Sorcery | #181 | delve top-7 to hand |
| Dredger's Insight | Enchantment | — | dies-trigger surveil-equivalent |
| Dress Down | Enchantment | #195 | lose-abilities + 1/1 base PT |
| Dryad Arbor | Land Creature | — | 1/1 Forest creature, no cost |
| Eldritch Evolution | Sorcery | TBD | sac-creature additional cost + tutor creature mv ≤ sac.mv+2 → battlefield + self-exile (CR 601.2f / 701.19a / 608.2) |
| Elegant Parlor | Land | — | R/W surveil dual |
| Endurance | Creature | TBD | MH2 incarnation: Flash + Reach + evoke pitch + ETB shuffle-graveyard-to-library |
| Engineered Explosives | Artifact | TBD | {X} Sunburst (charge counters via v1 X-provider) + {2}, sac: destroy each nonland permanent with mv = counters |
| Fiery Islet | Land | — | pay-1-life U/R + sac-draw |
| Force of Negation | Instant | #185 | pitch counter (non-creature) |
| Force of Will | Instant | #185 | pitch counter (universal) |
| Fury | Creature | — | evoke pitch + ETB X-damage divided |
| Galvanic Discharge | Instant | TBD | 1 + charge counters on artifacts/lands you control damage |
| Goblin Bombardment | Enchantment | — | sac-creature → 1 damage |
| Grief | Creature | #205 | evoke pitch + ETB discard |
| Grist, the Hunger Tide | Planeswalker | — | +1 token, -2 reanimate |
| Harbinger of the Seas | Creature | #157 | nonbasic-to-Island |
| Inspiring Vantage | Land | — | R/W fastland |
| Karn, the Great Creator | Planeswalker | TBD | opponent-artifact static + +1 animate + -2 wishboard |
| Karn Liberated | Planeswalker | TBD | +4 exile-from-hand, -3 exile-permanent; -14 restart deferred |
| Kraul Harpooner | Creature | — | fight-flyer shell |
| Lazotep Recruit | Creature | — | amass-keyword shell |
| Ledger Shredder | Creature | #193 | second-spell surveil + counter |
| Library Surveyor | Creature | — | ETB tutor shell |
| Liliana of the Veil | Planeswalker | #178 | +1 discard, -2 discard |
| Living End | Sorcery | TBD | Cascade + each-player mass-exile-grave + sac-creatures + mass-reanimate |
| Lurrus of the Dream-Den | Creature | TBD | Lifelink + cast-permanent-mv≤2-from-graveyard once per your turn (companion deck-rule deferred) |
| Magus of the Moon | Creature | #157 | nonbasic-to-Mountain |
| Mishra's Bauble | Artifact | — | sac → look + delayed draw |
| Mox Opal | Artifact | TBD | Legendary {0}: Metalcraft-gated any-color mana (CR 702.95) |
| Murktide Regent | Creature | #194 | delve cost + ETB X counters |
| Orcish Bowmasters | Creature | — | reactive ping shell |
| Pithing Needle | Artifact | #189 | name-targeted activated suppression |
| Phyrexian Tower | Land | — | {T}: {C} + {T}, sac creature: {B}{B} (Legendary) |
| Priest of Fell Rites | Creature | #196 | ETB reanimate + grave-unearth |
| Primeval Titan | Creature | TBD | Trample + ETB/attack tutor up to 2 lands tapped |
| Puresteel Paladin | Creature | TBD | Equipment-ETB draw trigger + zero-equip-cost on ≥3 artifacts |
| Ragavan, Nimble Pilferer | Creature | TBD | combat-damage Treasure + exile + may-cast EOT (CR 118.9 grant + ExileCastAlternativeCost); Dash deferred |
| Rift Bolt | Sorcery | #183 | suspend → 3 damage |
| Scavenging Ooze | Creature | #188 | exile-graveyard + counter + life |
| Scion of Draco | Artifact Creature | TBD | Domain cost-reduction {10} → {0} at 5 basic types (CR 702.16); keyword-grant rider deferred |
| Sea's Claim | Aura | #160 | enchanted land becomes Island |
| Sigarda's Aid | Enchantment | TBD | flash-grant equipment/aura + ETB auto-attach |
| Snapcaster Mage | Creature | #170 | flash + ETB flashback grant |
| Solitude | Creature | — | evoke pitch + ETB exile |
| Spreading Seas | Aura | #160 | retype land + draw |
| Spymaster's Vault | Land | — | B-source shell |
| Stoneforge Mystic | Creature | #184 | ETB tutor + activated put |
| Stubborn Denial | Instant | — | ferocious-conditional counter |
| Subtlety | Creature | TBD | evoke pitch + ETB bounce + look-and-bottom |
| Sunbaked Canyon | Land | — | pay-1-life R/W + sac-draw |
| Surgical Extraction | Instant | #192 | phyrexian global name exile |
| Sylvan Scrying | Sorcery | TBD | any-land tutor to hand (Tron enabler) |
| Tarmogoyf | Creature | #173 | CDA P/T from grave types |
| Teferi, Time Raveler | Planeswalker | #182 | sorcery-speed restriction emblem |
| Test Conniver | Creature | — | connive-keyword test card |
| Thundering Falls | Land | — | U/R surveil dual |
| Torpor Orb | Artifact | — | ETB-trigger suppression |
| Treasure Cruise | Sorcery | #181 | delve draw 3 |
| Tribal Flames | Sorcery | TBD | Domain X damage = distinct basic land types you control (CR 702.16) |
| Underground Mortuary | Land | — | U/B surveil dual |
| Unholy Heat | Instant | #190 | delirium variable damage |
| Up the Beanstalk | Enchantment | TBD | ETB draw + cast-MV-5+ draw |
| Urborg, Tomb of Yawgmoth | Land | #158 | grant Swamp to all lands |
| Urza's Mine | Land | TBD | Tron — {T}: {C}, {2} if all 3 Urza lands controlled |
| Urza's Power-Plant | Land | TBD | Tron — {T}: {C}, {2} if all 3 Urza lands controlled |
| Urza's Tower | Land | TBD | Tron — {T}: {C}, {2} if all 3 Urza lands controlled |
| Vexing Bauble | Artifact | — | sac-draw shell |
| Walking Ballista | Artifact Creature | — | grow + ping |
| Wastewood Verge | Land | — | B/G activation-gate land |
| Wrenn and Six | Planeswalker | #178 | +1 land return, -1 ping |
| Wrenn and Realmbreaker | Planeswalker | TBD | +1 mill 3 + may-return-land, -2 reanimate nonland permanent, -7 structural emblem |
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
| Cast-from-graveyard (Lurrus) | Done (TBD PR) | `Costs/GraveyardCastAlternativeCost.cs` + `CardData/Factories/LurrusOfTheDreamDenFactory.cs` (per-turn gate, mv ≤ 2, permanent-only) |
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
| Cascade | Done (TBD) | `Keywords/CascadeAction.cs` | Crashing Footfalls |
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
- **Murktide / Izzet Tempo** — High. Murktide Regent done, Counterspell done, Snapcaster Mage done, Lightning Bolt done, Expressive Iteration done, Ledger Shredder done, Consider done, Spell Pierce done, Subtlety done. Missing: Demilich absent. ~75%.
- **Mono-Green Tron** — High. Ancient Stirrings, Sylvan Scrying, Wurmcoil Engine done. Karn Liberated done. Karn, the Great Creator done. Tron lands (Urza's Mine + Tower + Power-Plant) done with the conditional {2} mana ability. ~70%.
- **Living End / Crashing Footfalls cascade** — High. Cascade keyword done (`Keywords/CascadeAction.cs`) + Crashing Footfalls shipped (#219). Living End shipped (this PR) with both the Cascade trigger and the resolve chain (per-player mass-exile-grave + sacrifice-creatures + mass-reanimate; ETB triggers fire on reanimated permanents via PR #174 plumbing). Suspend itself is done (#183). ~75%.
- **Rakdos Scam** — High. Grief done (#205, mirrors Solitude evoke + ETB pattern). Fury done (mirrors Solitude/Grief). Ragavan, Nimble Pilferer done (combat-damage Treasure + exile + may-cast EOT grant; Dash deferred). Dauthi Voidwalker done (Shadow + opponent-grave→exile-with-void-counter replacement effect + {2},{T},remove-void-counter activated cast-from-exile via CastFromExileAlternativeCost; EOT "this turn" timing on the cast permission deferred). Liliana of the Veil done, Fatal Push done, Thoughtseize done. ~75%.
- **Yawgmoth combo** — High. Yawgmoth done. Undying creatures (Young Wolf, Strangleroot Geist, Geralf's Messenger) done. Chord of Calling done. Eldritch Evolution done — tutor-with-sac is a primary engine starter for the deck. ~70%.
- **Domain Zoo** — Mid. Boros Charm done, fetches done, shocks done, Tribal Flames done, Scion of Draco's domain cost-reduction done (keyword-grant rider deferred). Territorial Kavu absent. ~45%.
- **Amulet Titan** — Mid. Amulet of Vigor done (untap-on-enters-tapped trigger) + Primeval Titan done (ETB + attack land-tutor for up to 2, tapped). No bounce lands. ~30%.
- **Lurrus Companion** — Low-mid. Lurrus of the Dream-Den done (Lifelink + once-per-turn cast-permanent-mv≤2-from-graveyard; companion deck-construction rule deferred). Pairs with the existing low-mv permanent suite (Mishra's Bauble, Dryad Arbor, Stoneforge Mystic, Walking Ballista, Dark Confidant, etc.). Deck-construction enforcement absent. ~30%.
- **Hammer Time / Equipment** — Mid-high. Stoneforge Mystic done. Colossus Hammer done (+10/+0 + lose flying via AttachedBoostEffect + new LoseKeywordEffect). Sigarda's Aid done (flash-grant on Equipment/Aura via FlashGrantRegistry + ETB-attach rider). Puresteel Paladin done (Equipment-ETB draw trigger + zero-equip-cost lifecycle binder that gates on ≥3 artifacts; equip-cost consumer wires up when an `EquipActivatedAbility` primitive lands). ~50%.
- **Merfolk** — Low. Aether Vial done (mana-free creature-cheater that's the deck's engine). Spreading Seas done. Lord of Atlantis, Master of the Pearl Trident, Silvergill Adept, Cursecatcher, Merfolk Trickster absent. ~20%.
- **Death and Taxes** — Low-mid. Aether Vial done (mana-free creature drop — the deck's signature). Stoneforge Mystic done. Solitude done. Thalia, Guardian of Thraben + Skyclave Apparition + Leonin Arbiter absent. ~30%.

## Top 20 Modern staples NOT yet implemented

Sorted by build priority (small infra lift × high meta share).

| # | Card | Difficulty | Blocker |
|---|---|---|---|
| 1 | (open) | — | Living End, Crashing Footfalls, and Cascade all landed; backlog needs a refresh against the next archetype-coverage pass. |

## How to update this doc

After merging a card-shipping PR:
1. Append the new card to the **Named factories** table (or **Template-covered (notable)** if it's a template port).
2. Remove it from **Top 20 not yet implemented** if it appears there.
3. Bump the **Last updated** date and **Latest origin/main** hash at the top.

After merging a mechanic-infra PR:
1. Flip the relevant row in **Costs**, **Effects**, **Keywords**, or **Targeting / cast flow** to **Done (#PR)**.
2. If a top-20 entry's blocker is resolved, drop its difficulty one tier in the priority table.

Keep this file terse. New cards = one row. New mechanics = one row. Long-form rationale lives in PR descriptions, not here.
