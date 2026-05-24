# Mechanic-dependency DAG

Scanner output: every `*Factory.cs` xmldoc / inline comment mentioning
`deferred` / `DEFERRED` / `blocked on` / `same gap`, clustered by canonical
engine primitive. Each row answers: "if we ship primitive _X_, which factory
xmldocs flagged that they're blocked on it?"

- **Generated:** 2026-05-24 18:31 UTC
- **Scanned dir:** `Majik.Core/CardData/Factories`
- **Total mentions:** 247
- **Clusters:** 17
- **Unclustered (need new registry pattern):** 131

Regenerate with `dotnet run --project Majik.Console -- mechanic-deps --md-out docs/MECHANIC_DEPS.md --json-out docs/mechanic-deps.json`.

## Priority queue

| Rank | Primitive | CR | Factories | Mentions |
|---:|---|---|---:|---:|
| 1 | Agent-prompt targeting MVP | — | 25 | 32 |
| 2 | Library shuffle (CR 701.20) | CR 701.20 | 12 | 17 |
| 3 | Token colour identity (CR 105 / CR 903.4) | CR 105 | 11 | 21 |
| 4 | "Activate only as a sorcery" gate (CR 117.1a) | CR 117.1a | 9 | 12 |
| 5 | Regeneration shield (CR 701.15) | CR 701.15 | 6 | 8 |
| 6 | Indestructible bypass on destroy (CR 702.12) | CR 702.12 | 6 | 7 |
| 7 | Layer-6 ability-grant subsystem (CR 613.1f) | CR 613.1f | 3 | 5 |
| 8 | Escape alt-cost (CR 702.143) | CR 702.143 | 2 | 2 |
| 9 | Kicker alt-cost (CR 702.33) | CR 702.33 | 2 | 2 |
| 10 | Class leveling (CR 716) | CR 716 | 1 | 2 |
| 11 | Manifest dread (CR 701.59) | CR 701.59 | 1 | 2 |
| 12 | Ascend / city's blessing (CR 702.131) | CR 702.131 | 1 | 1 |
| 13 | Cast-marker on Card | — | 1 | 1 |
| 14 | Companion (CR 702.139) | CR 702.139 | 1 | 1 |
| 15 | Equip activated-ability primitive (CR 702.6) | CR 702.6 | 1 | 1 |
| 16 | Gift (Bloomburrow) | — | 1 | 1 |
| 17 | Suspend alt-cost (CR 702.61) | CR 702.61 | 1 | 1 |

## Cluster detail

### 1. Agent-prompt targeting MVP

- **Blocks:** 25 factories (32 mentions)
- **Implementation hint:** IPlayerAgent needs ChooseTarget / ChooseYesNo surfaces; many spell factories punt on real targeting prompts.

Mentions:

- `NihilSpellbombFactory` (`NihilSpellbombFactory.cs:13`)
  > Full agent-prompt targeting deferred.
- `EsikasChariotFactory` (`EsikasChariotFactory.cs:12`)
  > Token-copy targeting auto-picks the first eligible token-creature the controller controls; agent-driven targeting is deferred.
- `MysticSanctuaryFactory` (`MysticSanctuaryFactory.cs:12`)
  > <b>"You may" prompt</b>: auto-takes the action when a target was supplied; agent-driven decline deferred (same posture as Snapcaster Mage / Tireless Tracker / Valakut).
- `AgathasSoulCauldronFactory` (`AgathasSoulCauldronFactory.cs:10`)
  > (Full targeting deferred — see below.
- `AgathasSoulCauldronFactory` (`AgathasSoulCauldronFactory.cs:65`)
  > Full targeting deferred (see xmldoc above).
- `SpellQuellerFactory` (`SpellQuellerFactory.cs:15`)
  > A "pick a spell from the stack" prompt is part of the broader agent-prompt MVP and is deferred.
- `SkyclaveApparitionFactory` (`SkyclaveApparitionFactory.cs:13`)
  > SetChosenTargets"/>; the agent prompt is deferred.
- `TerritorialKavuFactory` (`TerritorialKavuFactory.cs:12`)
  > If the controller has a card in hand: discard the first card (v1 deterministic; CR 701.16a agent-driven choice deferred — same posture as <see cref="PsychicFrogFactory"/> / Faithless Looting), then draw one.
- `TerritorialKavuFactory` (`TerritorialKavuFactory.cs:12`)
  > <b>Discard prompt</b>: v1 deterministically discards the first card in hand; agent-driven "choose which card to discard" deferred behind the same gate as Liliana / Faithless Looting / Psychic Frog.
- `TerritorialKavuFactory` (`TerritorialKavuFactory.cs:159`)
  > v1 first-card-in-hand pick (CR 701.16a — agent-driven choice deferred).
- `PriestOfFellRitesFactory` (`PriestOfFellRitesFactory.cs:12`)
  > <b>"You may" prompt</b>: the ETB trigger autopicks the first eligible creature card; declining and target-selection are deferred to the agent-prompt MVP.
- `EldritchEvolutionFactory` (`EldritchEvolutionFactory.cs:14`)
  > Full agent-driven sacrifice-target prompting requires the ITarget / TargetResolver pipeline (deferred — same gap noted on <see cref="SacrificeAnotherCreatureCost"/>).
- `PsychicFrogFactory` (`PsychicFrogFactory.cs:15`)
  > Agent-driven prompts are deferred behind the same queue as Liliana of the Veil + Faithless Looting + Sword of Feast and Famine.
- `PsychicFrogFactory` (`PsychicFrogFactory.cs:145`)
  > v1 deterministic first-card-in-hand pick per discard (CR 701.16a — agent-driven choice deferred).
- `FaithlessSalvagingFactory` (`FaithlessSalvagingFactory.cs:11`)
  > Real agent-driven "choose a card to discard" prompt deferred behind the same queue as Faithless Looting / Liliana of the Veil / Connive / Psychic Frog.
- `FaithlessSalvagingFactory` (`FaithlessSalvagingFactory.cs:109`)
  > Real agent-driven choice deferred.
- `PrimevalTitanFactory` (`PrimevalTitanFactory.cs:11`)
  > A first-class yes/no agent prompt is deferred (see <c>StoneforgeMystic</c> for the same gap).
- `RelicOfProgenitusFactory` (`RelicOfProgenitusFactory.cs:12`)
  > Agent prompt deferred.
- `RelicOfProgenitusFactory` (`RelicOfProgenitusFactory.cs:12`)
  > Full agent-prompt targeting deferred.
- `FaithlessLootingFactory` (`FaithlessLootingFactory.cs:119`)
  > Real agent-driven choice deferred.
- `UroTitanFactory` (`UroTitanFactory.cs:12`)
  > v1 always plays the first land in hand when one exists; a first-class yes/no agent prompt is deferred (same gap as Sun Titan / Primeval Titan / Stoneforge Mystic).
- `DauthiVoidwalkerFactory` (`DauthiVoidwalkerFactory.cs:13`)
  > Wiring an agent prompt mirrors the rest of the v1 factories (deferred).
- `SilvergillAdeptFactory` (`SilvergillAdeptFactory.cs:10`)
  > The actual enforcement at cast-time (agent prompt: reveal a Merfolk card from hand OR pay {3} as an additional cost) is deferred until the additional-cost framework supports reveal-based alternatives.
- `GoblinPiledriverFactory` (`GoblinPiledriverFactory.cs:10`)
  > Same shape as Primeval Titan's <c>selector</c> + Plague Engineer's <c>typeChooser</c> — agent-prompt integration is deferred.
- `SwordOfFeastAndFamineFactory` (`SwordOfFeastAndFamineFactory.cs:14`)
  > the damaged player discards a card — v1 deterministically picks the first card in hand (same v1 policy as <see cref="LilianaOfTheVeilFactory"/>'s +1 each-player-discards and <see cref="FaithlessLootingFactory"/>'s last-2-in-hand; agent prompt deferred); 2.
- `SwordOfFeastAndFamineFactory` (`SwordOfFeastAndFamineFactory.cs:14`)
  > Agent-driven "you choose which card you discard" (CR 701.16a — damaged player chooses) is deferred behind the same prompt queue as Liliana of the Veil + Faithless Looting.
- `YawgmothFactory` (`YawgmothFactory.cs:121`)
  > Full targeting deferred.
- `AetherVialFactory` (`AetherVialFactory.cs:12`)
  > <b>"You may" prompts</b>: both abilities auto-accept; declining and target-selection are deferred to the agent-prompt MVP.
- `GoblinLackeyFactory` (`GoblinLackeyFactory.cs:12`)
  > <b>"You may" prompt</b>: v1 auto-accepts when an eligible Goblin creature card exists in hand (same approach as Aether Vial's tap activated ability — declining the optional is deferred to the agent-prompt MVP).
- `AjaniNacatlPariahFactory` (`AjaniNacatlPariahFactory.cs:11`)
  > A real agent-driven yes/no + target prompt is deferred — same queue as Sun Titan / Stoneforge Mystic.
- `SunTitanFactory` (`SunTitanFactory.cs:11`)
  > The v1 effect always reanimates the first eligible permanent card when one exists; a first-class yes/no agent prompt is deferred (mirrors Priest of Fell Rites / Primeval Titan).
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:15`)
  > A real prompt-driven flow (and the "to it" bookkeeping that pins attachment to *that specific* token rather than any creature) is deferred behind the broader agent-prompt surface — same posture as Eternal Witness / Snapcaster Mage.

### 2. Library shuffle (CR 701.20)

- **CR citation:** CR 701.20
- **Blocks:** 12 factories (17 mentions)
- **Implementation hint:** Add IZone.Shuffle / ZoneService.ShuffleLibrary. Tutor-family factories all block on this single primitive.

Mentions:

- `ScapeshiftFactory` (`ScapeshiftFactory.cs:10`)
  > Library shuffle (CR 701.19c) deferred — no IZone.
- `ScapeshiftFactory` (`ScapeshiftFactory.cs:175`)
  > CR 701.19c — shuffle deferred (same rationale as SearchSpellFactory / PrimevalTitanFactory).
- `WishclawTalismanFactory` (`WishclawTalismanFactory.cs:146`)
  > CR 701.19c — shuffle deferred (no IZone.
- `StoneforgeMysticFactory` (`StoneforgeMysticFactory.cs:99`)
  > Shuffle and reveal- event emission are deferred (see class xmldoc).
- `StoneforgeMysticFactory` (`StoneforgeMysticFactory.cs:119`)
  > CR 701.19c shuffle deferred — see class xmldoc.
- `AssassinsTrophyFactory` (`AssassinsTrophyFactory.cs:11`)
  > Shuffle deferred — same MVP gap as every other tutor (<see cref="PathToExileFactory"/>).
- `GreenSunsZenithFactory` (`GreenSunsZenithFactory.cs:14`)
  > ## Deferred (v1 gaps)  - <b>Library shuffle</b> (CR 701.20a).
- `GreenSunsZenithFactory` (`GreenSunsZenithFactory.cs:185`)
  > Shuffle randomization itself is deferred (same gap as SearchSpellFactory).
- `EldritchEvolutionFactory` (`EldritchEvolutionFactory.cs:182`)
  > CR 701.19c — shuffle after a search effect (deferred — see class xmldoc / SearchSpellFactory).
- `GoblinEngineerFactory` (`GoblinEngineerFactory.cs:111`)
  > " v1: deterministic — take the first artifact card in the library; shuffle and reveal-event emission deferred (see class xmldoc).
- `GoblinEngineerFactory` (`GoblinEngineerFactory.cs:132`)
  > CR 701.19c shuffle deferred — see class xmldoc.
- `PrimevalTitanFactory` (`PrimevalTitanFactory.cs:101`)
  > CR 701.19a (search), CR 701.19c (shuffle deferred — see xmldoc).
- `SearchForTomorrowFactory` (`SearchForTomorrowFactory.cs:10`)
  > Library shuffle deferred — same gap as every other search effect in <see cref="SearchSpellFactory"/> (no IZone.
- `GoblinMatronFactory` (`GoblinMatronFactory.cs:138`)
  > CR 701.19c shuffle deferred — see class xmldoc.
- `TrinketMageFactory` (`TrinketMageFactory.cs:77`)
  > Shuffle and reveal-event emission are deferred (see class xmldoc).
- `TrinketMageFactory` (`TrinketMageFactory.cs:99`)
  > CR 701.19c shuffle deferred — see class xmldoc.
- `PonderFactory` (`PonderFactory.cs:58`)
  > The "may shuffle" rider is deferred (no-op).

### 3. Token colour identity (CR 105 / CR 903.4)

- **CR citation:** CR 105
- **Blocks:** 11 factories (21 mentions)
- **Implementation hint:** TokenFactory needs an explicit Colors field separate from mana cost; today tokens default to colourless.

Mentions:

- `BridgeFromBelowFactory` (`BridgeFromBelowFactory.cs:12`)
  > ## Deferred (v1 gaps)  - <b>Token-creature colour identity</b>: tokens carry subtype + keywords but no explicit colour today (same scope decision as Crashing Footfalls' "green" Rhinos, Wurmcoil's "colorless" Wurms).
- `BridgeFromBelowFactory` (`BridgeFromBelowFactory.cs:198`)
  > Colour identity ("black") is documented but the runtime token has no colour stamp (same gap as Crashing Footfalls / Pact of the Titan / Wurmcoil Engine).
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:12`)
  > <b>Token colour identity (blue + red)</b>: Mercenary token is created as colourless under the v1 token shape — same gap as Esika's Chariot's green Cats, Crashing Footfalls' green Rhinos, Pact of the Titan's red Giant.
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:120`)
  > " Token colour identity (blue + red) deferred — see class xmldoc.
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:145`)
  > Token colour identity (blue + red) deferred (see class xmldoc); Prowess pump on the token deferred (see class xmldoc).
- `EsikasChariotFactory` (`EsikasChariotFactory.cs:12`)
  > <b>Token colour identity (green)</b>: Cat tokens are created as colourless under the v1 token shape — same gap as Crashing Footfalls' green Rhinos and Wurmcoil's colourless Wurms.
- `EsikasChariotFactory` (`EsikasChariotFactory.cs:129`)
  > " Token colour identity (green) deferred — see class xmldoc.
- `EsikasChariotFactory` (`EsikasChariotFactory.cs:174`)
  > Token colour identity (green) is deferred (see class xmldoc).
- `MonasteryMentorFactory` (`MonasteryMentorFactory.cs:15`)
  > Token colour identity (white): tokens are created as colourless under the v1 token shape — same gap as Crashing Footfalls / Goblin Rabblemaster.
- `SkyclaveApparitionFactory` (`SkyclaveApparitionFactory.cs:13`)
  > v1 does not inject colour identity into tokens — the engine's token colour system (same gap as Crashing Footfalls' green Rhinos, Pact of the Titan's red Giant).
- `SkyclaveApparitionFactory` (`SkyclaveApparitionFactory.cs:199`)
  > NOTE (v1): token colour (blue) is not wired — same gap as Crashing Footfalls' green Rhinos / Pact of the Titan's red Giant.
- `GoblinRabblemasterFactory` (`GoblinRabblemasterFactory.cs:12`)
  > <b>Token colour identity (red)</b>: tokens are created as colourless under the v1 token shape — same gap as Pact of the Titan's "red" Giant and Crashing Footfalls' "green" Rhinos.
- `CrashingFootfallsFactory` (`CrashingFootfallsFactory.cs:15`)
  > The Trample + creature-type assignments match the printed text; the green colour identity is a downstream concern (same gap as Wurmcoil's "colorless" tokens, Solitude's "white" creatures, etc.
- `YoungPyromancerFactory` (`YoungPyromancerFactory.cs:13`)
  > Token colour identity (red): tokens are created as colourless under the v1 token shape — same gap as Goblin Rabblemaster / Crashing Footfalls.
- `BeastWithinFactory` (`BeastWithinFactory.cs:12`)
  > The token is a 3/3 green Beast creature token (CR 111.4 — token characteristics include colour; token "green" colour identity deferred — same gap as Crashing Footfalls' green Rhinos; token enters with <c>HasSummoningSickness = true</c> via <see cref="TokenFactory"/>).
- `BeastWithinFactory` (`BeastWithinFactory.cs:12`)
  > <b>Token colour (green)</b>: TokenFactory does not yet model token colour identity (same gap as Pact of the Titan's "red" Giant token, Crashing Footfalls' "green" Rhino tokens).
- `BeastWithinFactory` (`BeastWithinFactory.cs:129`)
  > Token colour (green) deferred — same gap as Pact of the Titan / Crashing Footfalls.
- `OcelotPrideFactory` (`OcelotPrideFactory.cs:14`)
  > <b>Token colour identity (white)</b>: Cat tokens are created as colourless under the v1 token shape — same gap as Esika's Chariot Cats / Crashing Footfalls Rhinos.
- `OcelotPrideFactory` (`OcelotPrideFactory.cs:200`)
  > Token colour identity (white) is deferred (see class xmldoc).
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:15`)
  > <b>Token colour identity (white)</b>: Monk token is colourless under the v1 token shape — same gap as Esika's Chariot's green Cats / Crashing Footfalls' green Rhinos / Pact of the Titan's red Giant.
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:187`)
  > Token colour identity (white) deferred — see class xmldoc.

### 4. "Activate only as a sorcery" gate (CR 117.1a)

- **CR citation:** CR 117.1a
- **Blocks:** 9 factories (12 mentions)
- **Implementation hint:** ActionValidator-side check: ability/spell flagged sorcery-speed only legal during own main phase with empty stack.

Mentions:

- `SwordOfFireAndIceFactory` (`SwordOfFireAndIceFactory.cs:209`)
  > Sorcery-speed restriction deferred (see class xmldoc).
- `WishclawTalismanFactory` (`WishclawTalismanFactory.cs:110`)
  > CR 117.1a sorcery-speed restriction deferred (see class xmldoc).
- `SkullclampFactory` (`SkullclampFactory.cs:13`)
  > ## Deferred  - <b>Sorcery-speed restriction</b> on Equip activation (CR 702.6a) — same gap as <see cref="ColossusHammerFactory"/>.
- `SkullclampFactory` (`SkullclampFactory.cs:159`)
  > Sorcery-speed restriction deferred (see class xmldoc).
- `ColossusHammerFactory` (`ColossusHammerFactory.cs:111`)
  > Sorcery-speed restriction deferred (see class xmldoc).
- `UmezawasJitteFactory` (`UmezawasJitteFactory.cs:16`)
  > ## Deferred  - <b>Sorcery-speed restriction</b> on Equip activation (CR 702.6a) — same gap as <see cref="ColossusHammerFactory"/>.
- `UmezawasJitteFactory` (`UmezawasJitteFactory.cs:246`)
  > Sorcery-speed restriction deferred (see class xmldoc).
- `TirelessTrackerFactory` (`TirelessTrackerFactory.cs:14`)
  > <b>"Activate only as a sorcery"</b> — Tireless Tracker's printed activated ability has NO sorcery-speed restriction (instant speed on the official card), so nothing is deferred here for this card.
- `TasigurTheGoldenFangFactory` (`TasigurTheGoldenFangFactory.cs:114`)
  > CR 117.1a sorcery-speed restriction deferred (see class xmldoc).
- `SwordOfFeastAndFamineFactory` (`SwordOfFeastAndFamineFactory.cs:14`)
  > ## Deferred  - <b>Sorcery-speed restriction</b> on Equip activation (CR 702.6a) — same gap as the rest of the equipment cycle; enforcement belongs in an action-validator gate, not on the ability itself.
- `SwordOfFeastAndFamineFactory` (`SwordOfFeastAndFamineFactory.cs:244`)
  > Sorcery-speed restriction deferred (see class xmldoc).
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:225`)
  > Sorcery-speed restriction deferred (see class xmldoc).

### 5. Regeneration shield (CR 701.15)

- **CR citation:** CR 701.15
- **Blocks:** 6 factories (8 mentions)
- **Implementation hint:** ReplacementBus filter on ZoneMoveIntent battlefield→graveyard, gated by sourceCard.HasAbility("Regenerate").

Mentions:

- `DrownInTheLochFactory` (`DrownInTheLochFactory.cs:12`)
  > Indestructible / regeneration riders are deferred (same gap as <see cref="SlaughterPactFactory"/> and the rest of the single-target destroy family).
- `DrownInTheLochFactory` (`DrownInTheLochFactory.cs:210`)
  > CR 701.7 — destroy → owner's graveyard (Indestructible / regeneration deferred, same gap as SlaughterPactFactory).
- `TerminateFactory` (`TerminateFactory.cs:58`)
  > The "it can't be regenerated" rider is deferred — the engine has no regeneration shield surface in v1 (see class xmldoc).
- `TerminateFactory` (`TerminateFactory.cs:100`)
  > "It can't be regenerated" rider is deferred — no regeneration shield surface in the engine yet (same gap as Wrath of God / Day of Judgment's can't-regenerate clause).
- `MurderousRiderFactory` (`MurderousRiderFactory.cs:147`)
  > Indestructible / regeneration deferred (same gap as SlaughterPact).
- `MurderousCutFactory` (`MurderousCutFactory.cs:11`)
  > 1 target-creature request; "indestructible" + "can't be regenerated" riders deferred — same lossy MVP as <c>DestroySpellFactory.
- `BeastWithinFactory` (`BeastWithinFactory.cs:124`)
  > Indestructible / regeneration rider deferred (same gap as Terminate / Abrupt Decay / Slaughter Pact).
- `EmberethShieldbreakerFactory` (`EmberethShieldbreakerFactory.cs:126`)
  > Indestructible / regeneration deferred (same gap as SlaughterPact).

### 6. Indestructible bypass on destroy (CR 702.12)

- **CR citation:** CR 702.12
- **Blocks:** 6 factories (7 mentions)
- **Implementation hint:** Destroy intent should consult a HasIndestructible() predicate before issuing ZoneMoveIntent → graveyard.

Mentions:

- `TerminateFactory` (`TerminateFactory.cs:11`)
  > <b>Indestructible</b>: the destroy call moves the creature to the graveyard without checking for Indestructible — same gap as every other single-target destroy template.
- `KolaghansCommandFactory` (`KolaghansCommandFactory.cs:249`)
  > Indestructible rider deferred.
- `AssassinsTrophyFactory` (`AssassinsTrophyFactory.cs:11`)
  > <b>Indestructible</b>: the destroy call moves the permanent to the graveyard without checking for Indestructible (same gap as every other single-target destroy template — Terminate, Abrupt Decay, Slaughter Pact).
- `AssassinsTrophyFactory` (`AssassinsTrophyFactory.cs:133`)
  > Indestructible rider deferred (same gap as Terminate / Abrupt Decay / Slaughter Pact).
- `WrathOfTheSkiesFactory` (`WrathOfTheSkiesFactory.cs:12`)
  > ## v1 simplifications  - <b>Indestructible bypass</b>: same gap as <see cref="WrathOfGodFactory"/> / <see cref="EngineeredExplosivesFactory"/> — <see cref="OracleSpellBinder.
- `BeastWithinFactory` (`BeastWithinFactory.cs:12`)
  > <b>Indestructible / regeneration</b>: the destroy call moves the permanent to the graveyard without checking for Indestructible or an active regeneration shield (same gap as every other single-target destroy template — Terminate, Abrupt Decay, Slaughter Pact).
- `AbruptDecayFactory` (`AbruptDecayFactory.cs:11`)
  > <b>Indestructible</b>: the destroy call moves the permanent to the graveyard without checking for Indestructible — same gap as every other single-target destroy template (Slaughter Pact, Force of Vigor destroy path, etc.

### 7. Layer-6 ability-grant subsystem (CR 613.1f)

- **CR citation:** CR 613.1f
- **Blocks:** 3 factories (5 mentions)
- **Implementation hint:** ContinuousEffectsService needs an 'ability grant' layer so 'gains <ability>' effects flow through GetAbilities() at read time.

Mentions:

- `SwordOfFireAndIceFactory` (`SwordOfFireAndIceFactory.cs:146`)
  > " (CR 702.16) Markers ride on the equipment card itself; a Layer 6 grant re-projecting them onto the equipped creature is deferred (see class xmldoc).
- `AgathasSoulCauldronFactory` (`AgathasSoulCauldronFactory.cs:10`)
  > The layer-6 continuous effect that actually grants those abilities to battlefield creatures is deferred until the layer-6 ability-grant subsystem is in place.
- `AgathasSoulCauldronFactory` (`AgathasSoulCauldronFactory.cs:88`)
  > CR 702.49 — imprint: record this creature card on the Cauldron so the ability-grant static ability can reference it later (layer-6 grant deferred; storage wired here).
- `BloodghastFactory` (`BloodghastFactory.cs:14`)
  > A proper continuous effect (Layer 6 keyword grant gated on a live life-total predicate) is deferred until the conditional-keyword CDA surface exists.
- `BloodghastFactory` (`BloodghastFactory.cs:126`)
  > A full dynamic Layer 6 conditional keyword grant is deferred — see class xmldoc.

### 8. Escape alt-cost (CR 702.143)

- **CR citation:** CR 702.143
- **Blocks:** 2 factories (2 mentions)
- **Implementation hint:** Cast-from-graveyard alt cost that additionally exiles N cards. Sibling of Flashback's cast-from-graveyard, but with the extra exile-cost rider.

Mentions:

- `ClingToDustFactory` (`ClingToDustFactory.cs:11`)
  > ## Deferred (v1 gaps)  - Escape alt-cost ({2}{B}, exile two other graveyard cards — CR 702.143) deferred — same gap as Uro / Phlage, blocked on the missing graveyard-cast alt-cost + multi-card-exile additional-cost primitive.
- `UroTitanFactory` (`UroTitanFactory.cs:12`)
  > The printed "unless it escaped" rider is structurally collapsed — Escape (CR 702.143) is not wired in v1 (see deferred section), so a hardcast Uro is always sacrificed by this trigger, faithful to the printed text in the non-escape case.

### 9. Kicker alt-cost (CR 702.33)

- **CR citation:** CR 702.33
- **Blocks:** 2 factories (2 mentions)
- **Implementation hint:** Optional additive cast cost on a spell; flips a runtime 'kicked' flag the rest of the spell pipeline can branch on.

Mentions:

- `SlickshotShowOffFactory` (`SlickshotShowOffFactory.cs:10`)
  > Same posture as <see cref="BurstLightningFactory"/>'s deferred Kicker rider — ship the printed shape + the most common triggered/static body, defer the alt-cost mechanic until its primitive lands.
- `BurstLightningFactory` (`BurstLightningFactory.cs:10`)
  > "  ## Implementation (v1 — kicker primitive deferred)  CR 702.33 — Kicker is an additional cost (not an alternative cost) that modifies the spell's effect when paid.

### 10. Class leveling (CR 716)

- **CR citation:** CR 716
- **Blocks:** 1 factories (2 mentions)
- **Implementation hint:** Class enchantment level-up cost + per-level static/triggered ability accretion.

Mentions:

- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:12`)
  > <b>Level 2 cast-trigger</b> ("Whenever you cast a noncreature spell, the Mercenary deals 1 damage to any target"): DEFERRED with the leveling primitive.
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:12`)
  > <b>Level 3 cast-trigger</b> ("Whenever you cast a noncreature spell, draw a card, then discard a card"): DEFERRED with the leveling primitive.

### 11. Manifest dread (CR 701.59)

- **CR citation:** CR 701.59
- **Blocks:** 1 factories (2 mentions)
- **Implementation hint:** Manifest the top card face-down + scry-style discard rider. Sibling of vanilla Manifest with an added zone-choice prompt.

Mentions:

- `AbhorrentOculusFactory` (`AbhorrentOculusFactory.cs:13`)
  > ## Deferred (v1 gaps — manifest dread is a stub)  - <b>Manifest dread</b> (CR 701.59 / Duskmourn): the printed resolution effect — look at top two of library, put one onto the battlefield face down as a 2/2 manifest creature and the other into your graveyard — is wired as a structural stub.
- `AbhorrentOculusFactory` (`AbhorrentOculusFactory.cs:13`)
  > CR rule references: 205.3m (Eye subtype), 601.2f (additional cost), 603.1 / 500.4 (upkeep trigger), 702.9 (Flying), 701.59 (manifest dread — deferred).

### 12. Ascend / city's blessing (CR 702.131)

- **CR citation:** CR 702.131
- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** Per-player flag + state-based check (≥10 permanents). Static abilities then key off blessing-active predicate.

Mentions:

- `OcelotPrideFactory` (`OcelotPrideFactory.cs:14`)
  > The attack trigger ships with the gate stubbed (always 1 token); the "doubled to 2" half of the printed text is deferred until an Ascend primitive lands.

### 13. Cast-marker on Card

- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** Persistent 'this object was cast (vs. put onto the battlefield)' flag — Bloodghast, The One Ring, Pact triggers all key off it.

Mentions:

- `TheOneRingFactory` (`TheOneRingFactory.cs:15`)
  > The effect body is a no-op — the "if you cast it" intervening-if clause, the "until your next turn" expiry, and the "protection from everything" player-scoped grant are all deferred (no cast-marker on Card, no per-player delayed cleanup, no Player.

### 14. Companion (CR 702.139)

- **CR citation:** CR 702.139
- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** Deck-construction check + 'cast from outside the game' once-per-game pipeline.

Mentions:

- `LurrusOfTheDreamDenFactory` (`LurrusOfTheDreamDenFactory.cs:12`)
  > ## Companion (DEFERRED) The companion deck-construction rule (CR 702.139 — "Each permanent card in your starting deck has mana value 2 or less") is foundational to the deck-builder, not the runtime, and is intentionally NOT enforced here.

### 15. Equip activated-ability primitive (CR 702.6)

- **CR citation:** CR 702.6
- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** EquipActivatedAbility — sorcery-speed activation, attaches/re-attaches Equipment to a chosen creature.

Mentions:

- `PuresteelPaladinFactory` (`PuresteelPaladinFactory.cs:11`)
  > ## Deferred (v1 gaps)  - <b>Equip-ability primitive</b>: the engine has no <c>EquipActivatedAbility</c> primitive yet — Equipment cards currently don't model their printed "Equip {N}" activated ability at all (Stoneforge Mystic's activated ability is a separate "put-an-Equipment-from-hand" effect, not an equip activati…

### 16. Gift (Bloomburrow)

- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** Cast-time choice: a static/triggered side effect granting an opponent a defined gift (treasure, draw, etc.).

Mentions:

- `IntoTheFloodMawFactory` (`IntoTheFloodMawFactory.cs:12`)
  > ## Deferred (v1 gaps) — Gift mechanic (CR 701.59 in the 2024 errata) The "Gift a tapped Fish" clause is a cast-time choice that lets the caster promise an opponent a gift; if promised, the opponent creates a tapped 1/1 blue Fish creature token BEFORE the spell's other effects, and Into the Flood Maw's target predicate …

### 17. Suspend alt-cost (CR 702.61)

- **CR citation:** CR 702.61
- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** Exile-from-hand alt cost with time counters; upkeep auto-cast when last counter is removed.

Mentions:

- `UroTitanFactory` (`UroTitanFactory.cs:12`)
  > Same shape as the deferred Boromir / suspend cost primitives.

## Unclustered (need new registry pattern)

- `SplinterTwinFactory` (`SplinterTwinFactory.cs:16`)
  > ## Deferred (v1 gaps)  - <b>Layer 1 copy effect</b>: the token's P/T + keywords are snapshotted at the moment the ability resolves; if the bearer's characteristics change later (counters, +1/+1 boost, lord anthems), the token does NOT track them.
- `ScapeshiftFactory` (`ScapeshiftFactory.cs:10`)
  > ## v1 gaps - <b>"Any number" prompt</b>: the engine has no first-class "pick a subset of permanents to sacrifice" agent hook.
- `ScapeshiftFactory` (`ScapeshiftFactory.cs:10`)
  > <b>Library shuffle</b> (CR 701.19c) — same gap as the rest of the tutor surface.
- `TheOneRingFactory` (`TheOneRingFactory.cs:121`)
  > " Structural: "if you cast it" + "until your next turn" expiry deferred — see class xmldoc.
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:12`)
  > Blockers: (1) no per-activated-ability sorcery-speed gate yet (same gap as Tasigur, the Golden Fang's {B}{G}{U} activation, Wishclaw Talisman's tutor, Priest of Fell Rites' reanimate); (2) no Class-level tracker bound to the card via a binder analogous to <see cref="CardData.
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:12`)
  > Same v1 gap as <see cref="MonasteryMentorFactory"/>'s spawned Monk tokens (see that factory's xmldoc for the broader plan).
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:120`)
  > Prowess pump on token deferred — keyword marker only, see class xmldoc.
- `NihilSpellbombFactory` (`NihilSpellbombFactory.cs:13`)
  > Real prompt deferred until IPlayerAgent grows a ChooseYesNoAsync surface.
- `SwordOfFireAndIceFactory` (`SwordOfFireAndIceFactory.cs:15`)
  > The shipped markers are inspectable on the card so tests + bot heuristics can read intent, with full DEBT-A enforcement (CR 702.16e — damage / enchanting / equipping / blocking / targeting) deferred behind that grant-on-attach work.
- `SwordOfFireAndIceFactory` (`SwordOfFireAndIceFactory.cs:15`)
  > <b>Sorcery-speed restriction</b> on Equip activation (CR 702.6a) — same gap as <see cref="ColossusHammerFactory"/>.
- `TerminateFactory` (`TerminateFactory.cs:11`)
  > <b>"It can't be regenerated"</b>: the engine has no regeneration shield surface in v1 (same gap as Wrath of God's and DayOfJudgment's can't-be-regenerated rider, and SlaughterPact's indestructible/regeneration note).
- `IzzetCharmFactory` (`IzzetCharmFactory.cs:211`)
  > Real agent- driven "choose 2 cards to discard" prompt is deferred — same queue as Faithless Looting / Liliana / Connive.
- `ConversionFactory` (`ConversionFactory.cs:54`)
  > The upkeep sacrifice clause is deferred — only the type-change is live.
- `MysticSanctuaryFactory` (`MysticSanctuaryFactory.cs:12`)
  > CanBePutOnStack"/> runs it at stack-push time; a second recheck at resolution is deferred.
- `AshiokDreamRenderFactory` (`AshiokDreamRenderFactory.cs:11`)
  > Enforcement at the actual library-search sites is DEFERRED (same gap as <see cref="LeoninArbiterSearchRestrictionEffect"/>): the engine currently lacks a unified library-search surface that enforcement could hook.
- `AshiokDreamRenderFactory` (`AshiokDreamRenderFactory.cs:11`)
  > MoveCardTo"/> and consult the <see cref="ReplacementBus"/> — every direct-zone-mutation path that bypasses the bus also bypasses this rider, same gap as Anger of the Gods / Containment Priest.
- `AshiokDreamRenderFactory` (`AshiokDreamRenderFactory.cs:146`)
  > " -------------------- Structural marker on the ContinuousEffectsService — enforcement at library-search sites is deferred (see xmldoc note above and LeoninArbiterSearchRestrictionEffect's parallel gap).
- `AshiokDreamRenderFactory` (`AshiokDreamRenderFactory.cs:183`)
  > The <em>enforcement</em> of the search restriction is DEFERRED — the engine presently lacks a unified library-search surface to hook (same gap as <see cref="LeoninArbiterSearchRestrictionEffect"/>).
- `AgathasSoulCauldronFactory` (`AgathasSoulCauldronFactory.cs:10`)
  > )  ## Deferred (v1 gaps)
- `CreepingTarPitFactory` (`CreepingTarPitFactory.cs:11`)
  > Without the bus (single-arg dispatcher path), the land enters untapped — deferred to the production binder layer (mirrors every other always-tapped factory path in this codebase).
- `MonasteryMentorFactory` (`MonasteryMentorFactory.cs:15`)
  > Monk tokens are "with prowess" — prowess on the token is deferred (same gap as Goblin Rabblemaster's token keyword wiring).
- `MonasteryMentorFactory` (`MonasteryMentorFactory.cs:162`)
  > "With prowess" on the token is deferred — see factory xmldoc.
- `WastewoodVergeFactory` (`WastewoodVergeFactory.cs:8`)
  > {T}: Add {B} mana ability — wired (restriction deferred; see below).
- `WastewoodVergeFactory` (`WastewoodVergeFactory.cs:47`)
  > " v1: restriction deferred — ability activates unconditionally.
- `TendrilsOfAgonyFactory` (`TendrilsOfAgonyFactory.cs:10`)
  > CR 702.40a's "you may choose new targets for the copies" rider is deferred (see <see cref="StormHelper"/> + <see cref="Majik.
- `TendrilsOfAgonyFactory` (`TendrilsOfAgonyFactory.cs:10`)
  > <b>Action validator filtering</b>: target list is "target opponent"; the agent's pick is honoured verbatim (no extra opponent-only filtering yet — same gap as <see cref="GriefFactory"/>).
- `AngerOfTheGodsFactory` (`AngerOfTheGodsFactory.cs:10`)
  > ## Why a named factory (over the existing template) The shared <c>DealsDamageEachCreatureTemplate</c> already binds the first sentence of Anger of the Gods by shape, but it (a) scans only the caster's battlefield (same gap as Pyroclasm — see that factory) and (b) doesn't carry the "would die → exile" rider at all.
- `NecrodominanceFactory` (`NecrodominanceFactory.cs:15`)
  > Same v1 gap as the discard→exile marker on Necropotence: shape-correct, not live.
- `NecrodominanceFactory` (`NecrodominanceFactory.cs:15`)
  > Cards in exile are inspectable to all observers in v1, so this clause is a no-op gain — same gap as Dauthi Voidwalker.
- `NecrodominanceFactory` (`NecrodominanceFactory.cs:155`)
  > Face-down exile is deferred — engine has no face-down flag.
- `MalevolentRumbleFactory` (`MalevolentRumbleFactory.cs:10`)
  > Peek up to top 4 cards of the caster's library (CR 701.15 reveal is folded into the same peek — no <c>CardsRevealedEvent</c> fires yet; same gap as Ancient Stirrings / Atraxa / Goblin Matron).
- `VeilOfSummerFactory` (`VeilOfSummerFactory.cs:13`)
  > Player-side hexproof (the "you" half of the clause) requires player-keyword infrastructure not in the engine today and is deferred.
- `SpellQuellerFactory` (`SpellQuellerFactory.cs:137`)
  > The "pick a spell from the stack" prompt is deferred to the agent MVP.
- `TerritorialKavuFactory` (`TerritorialKavuFactory.cs:12`)
  > <b>"You may" prompt on the attack trigger</b>: v1 always takes the loot when a card is available; an explicit yes/no prompt is deferred.
- `GristFactory` (`GristFactory.cs:7`)
  > ## V1 simplification The "not on the battlefield" conditional is deferred.
- `GristFactory` (`GristFactory.cs:7`)
  > The conditional layer-4 effect ("only when not on battlefield") is documented but deferred to a future slice.
- `GristFactory` (`GristFactory.cs:50`)
  > The oracle-text restriction ("as long as … isn't on the battlefield") is a conditional layer-4 effect — deferred.
- `KarnLiberatedFactory` (`KarnLiberatedFactory.cs:140`)
  > v1 DEFERRED — shipped as a no-op so the loyalty change (and "this card is a legal -14 ability") still apply (CR 606.3).
- `AmpedRaptorFactory` (`AmpedRaptorFactory.cs:252`)
  > ) — same gap as every "cast for free from exile" hook today.
- `NobleHierarchFactory` (`NobleHierarchFactory.cs:12`)
  > <b>Live combat-attackers provider</b>: same gap as Goblin Piledriver.
- `MurderousRiderFactory` (`MurderousRiderFactory.cs:12`)
  > " (LTB exile clause deferred — see Deferred section.
- `MurderousRiderFactory` (`MurderousRiderFactory.cs:12`)
  > Adding this requires the same replacement-effect surface used by the Anger of the Gods exile rider (see <see cref="AngerOfTheGodsFactory"/>); deferred to keep the v1 ship minimal.
- `MurderousRiderFactory` (`MurderousRiderFactory.cs:12`)
  > MoveToGraveyard"/>; same gap as <see cref="SlaughterPactFactory"/> and the rest of the single-target destroy family.
- `PriestOfFellRitesFactory` (`PriestOfFellRitesFactory.cs:12`)
  > </para>  ## Deferred (v1 gaps)
- `PriestOfFellRitesFactory` (`PriestOfFellRitesFactory.cs:132`)
  > Guard: only fire when the Priest is currently in its owner's graveyard, so spurious activations from other zones are no-op-shaped while engine zone-scoping is deferred.
- `PriestOfFellRitesFactory` (`PriestOfFellRitesFactory.cs:152`)
  > Skip if not currently in graveyard — activation is illegal from other zones (engine gating deferred; the guard keeps shape tests honest).
- `ThroughTheBreachFactory` (`ThroughTheBreachFactory.cs:12`)
  > Through the Breach is still castable for its printed cost; the splice rider is structural-only on the oracle text and will be added when the engine has an Arcane- spell awareness pass (same gap as every other Splice card).
- `LilianaOfTheVeilFactory` (`LilianaOfTheVeilFactory.cs:118`)
  > v1 deferred — loyalty change applies with an empty body so the cost is still paid.
- `ReflectorMageFactory` (`ReflectorMageFactory.cs:149`)
  > DEFERRED: "That creature's owner can't cast spells with the same name as that creature until your next turn.
- `WrennsResolveFactory` (`WrennsResolveFactory.cs:11`)
  > Multi-player turn-skipping nuances deferred.
- `WallOfRootsFactory` (`WallOfRootsFactory.cs:11`)
  > No deferred work; behaviour matches the printed card.
- `SylvanScryingFactory` (`SylvanScryingFactory.cs:8`)
  > The picked land moves Library → Hand without publishing a reveal event; same gap as Stoneforge Mystic's ETB tutor.
- `MasterOfThePearlTridentFactory` (`MasterOfThePearlTridentFactory.cs:9`)
  > The combat-validator enforcement of Islandwalk ("creature can't be blocked as long as the defending player controls an Island") is deferred — same posture as Intimidate / Menace enforcement.
- `PerniciousDeedFactory` (`PerniciousDeedFactory.cs:10`)
  > </item> </list>  ## Deferred (v1 gaps)
- `SigardasAidFactory` (`SigardasAidFactory.cs:11`)
  > <b>Target creature prompt</b>: "target creature you control" auto-picks the first controller-side creature (CR 701.3a target prompt is deferred — same v1 simplification as Stoneforge Mystic's attach step).
- `SigardasAidFactory` (`SigardasAidFactory.cs:145`)
  > "target creature you control" — v1 auto-picks the first creature on the controller's battlefield (CR 701.3a prompt deferred — same simplification as Stoneforge Mystic's attach step).
- `ChordOfCallingFactory` (`ChordOfCallingFactory.cs:14`)
  > ## Deferred (v1 gaps)  - <b>Convoke cost reduction</b>.
- `DryadArborFactory` (`DryadArborFactory.cs:7`)
  > Green Sun's Zenith interaction (can be fetched as a Forest creature — deferred to the targeting / land-subtype search slice).
- `SpriteDragonFactory` (`SpriteDragonFactory.cs:12`)
  > ## Deferred (v1 gaps)  - <b>Continuous P/T recomputation</b> — Sprite Dragon's effective P/T is derived from base 1/1 plus +1/+1 counters via the standard <see cref="CounterCollection"/> path (CR 613.4 layer 7d), inherited from every other +1/+1-counter user (Psychic Frog activated ability, Ledger Shredder surveil ride…
- `PsychicFrogFactory` (`PsychicFrogFactory.cs:15`)
  > ## Deferred (v1 gaps)  - <b>Discard prompt</b> on the loot half and the activation cost (CR 701.16a — discarding player chooses) — v1 deterministically picks the first card in hand.
- `LeoninArbiterFactory` (`LeoninArbiterFactory.cs:80`)
  > Actual search enforcement is deferred pending a unified library-search hook (see class xmldoc).
- `FuryFactory` (`FuryFactory.cs:13`)
  > <b>Card-source threading on damage events</b>: emitting <see cref="DamageDealtEvent"/> with a proper source card requires plumbing the resolving permanent into the trigger effect — deferred for parity with Solitude's lifelink wiring.
- `FaithlessSalvagingFactory` (`FaithlessSalvagingFactory.cs:11`)
  > "Discard a creature card" pick prompt — same gap as the resolve-time discard.
- `RoilingVortexFactory` (`RoilingVortexFactory.cs:17`)
  > The sacrifice payment in the current <see cref="AdditionalCost"/> implementation is a no-op stub (zone move deferred to a future zone-service refactor — same gap noted on <see cref="RelicOfProgenitusFactory"/>) so the activated ability does NOT move Vortex to its owner's graveyard in v1.
- `RoilingVortexFactory` (`RoilingVortexFactory.cs:215`)
  > The sacrifice payment is a no-op stub in v1 (same gap as Relic of Progenitus / Nihil Spellbomb), so the activated ability does not actually graveyard Vortex; future zone-service refactor unifies it.
- `MysticalTutorFactory` (`MysticalTutorFactory.cs:13`)
  > The picked card moves Library → top-of-Library without publishing a reveal event; same gap as the other search factories.
- `ColossusHammerFactory` (`ColossusHammerFactory.cs:10`)
  > Real targeting prompt deferred.
- `ManamorphoseFactory` (`ManamorphoseFactory.cs:9`)
  > Net mana-effect bookkeeping for cost-reduction restrictions (CR 106.11b — Manamorphose generates two mana while costing two, so it is net-zero) isn't tracked because the engine has no mana-provenance ledger yet (same gap as Cavern of Souls' spend-restriction).
- `AnimateDeadFactory` (`AnimateDeadFactory.cs:14`)
  > ## Deferred (v1 gaps)  - <b>Real "Enchant creature card in a graveyard" cast-target API</b>: the engine's Aura target plumbing is <see cref="Permanent"/>-typed (CR 303.4a), so the graveyard target is surfaced via a bespoke <see cref="TargetRequest"/> populated with <see cref="Creature"/> <i>cards</i>.
- `AnimateDeadFactory` (`AnimateDeadFactory.cs:14`)
  > <b>Sorcery-speed cast restriction</b>: not enforced — same gap as every other Aura factory in this repo.
- `ManaVaultFactory` (`ManaVaultFactory.cs:110`)
  > The v1 "may" collapses to "pay-if-able"; the prompt surface to decline is the same gap shared with the pact-cycle factories.
- `RelicOfProgenitusFactory` (`RelicOfProgenitusFactory.cs:79`)
  > On resolve: auto-pick first card from target player's graveyard and exile it (v1 deterministic; real agent-pick deferred).
- `PactOfNegationFactory` (`PactOfNegationFactory.cs:14`)
  > Multi-player turn-skipping nuances deferred.
- `UroTitanFactory` (`UroTitanFactory.cs:12`)
  > ("Elder" creature subtype is not yet in <see cref="CardSubtype"/> — Giant is wired; Elder is deferred — see gaps below.
- `UroTitanFactory` (`UroTitanFactory.cs:12`)
  > <see cref="CardSubtype"/> only carries Giant; Elder is not yet in the enum, mirroring the same gap for other "Elder X" creatures (Elder Dragons etc).
- `BloodghastFactory` (`BloodghastFactory.cs:14`)
  > "You may" prompt: auto-accepted (same gap as Arclight Phoenix / Sneak Attack / Tireless Tracker).
- `KarnScionOfUrzaFactory` (`KarnScionOfUrzaFactory.cs:12`)
  > <b>-1</b>: DEFERRED to a no-op body (loyalty change still applies per CR 606.3).
- `KarnScionOfUrzaFactory` (`KarnScionOfUrzaFactory.cs:12`)
  > TargetRequest"/>s yet, so the opponent picker for the +1 isn't agent-driven (same gap as <see cref="WrennAndRealmbreakerFactory"/>).
- `KarnScionOfUrzaFactory` (`KarnScionOfUrzaFactory.cs:12`)
  > <b>Token colour</b>: Construct token is created with no colour-set primitive (matches Wurmcoil Engine + Crashing Footfalls token v1 gap — `CardColors.
- `KarnScionOfUrzaFactory` (`KarnScionOfUrzaFactory.cs:168`)
  > DEFERRED — requires "exiled with this source" tag tracking on exiled cards.
- `NecropotenceFactory` (`NecropotenceFactory.cs:14`)
  > Auto-cleanup mirrors the <see cref="DauthiVoidwalkerFactory"/> v1 gap.
- `NecropotenceFactory` (`NecropotenceFactory.cs:163`)
  > Face-down exile is deferred — engine has no face-down flag.
- `MishrasBaubleFactory` (`MishrasBaubleFactory.cs:12`)
  > Multi-player turn-skipping semantics deferred.
- `BadgermoleCubFactory` (`BadgermoleCubFactory.cs:51`)
  > No abilities attached in v1 — both are deferred (see xmldoc above).
- `DauthiVoidwalkerFactory` (`DauthiVoidwalkerFactory.cs:13`)
  > Cleanup) and is deferred until a cast-permission flag is added.
- `DauthiVoidwalkerFactory` (`DauthiVoidwalkerFactory.cs:117`)
  > Unregister{TIntent}"/> when Voidwalker leaves the battlefield (v1 — automatic leave-cleanup deferred).
- `DauthiVoidwalkerFactory` (`DauthiVoidwalkerFactory.cs:140`)
  > Combat ability: can block / be blocked only by creatures with Shadow.
- `GoblinMatronFactory` (`GoblinMatronFactory.cs:12`)
  > The picked card moves Library → Hand without publishing a CardRevealedEvent; same gap as the other tutor factories.
- `AbruptDecayFactory` (`AbruptDecayFactory.cs:11`)
  > <b>Can't be countered</b> — a <see cref="KeywordAbility"/> marker "Can't Be Countered" is attached to the card shape (structural; actual enforcement via SpellCaster / StackResolver is deferred — same posture as Veil of Summer's turn-scoped uncounterable rider and Force of Will's text interaction).
- `AbruptDecayFactory` (`AbruptDecayFactory.cs:11`)
  > </item> </list>  ## Deferred (v1 gaps)
- `AbruptDecayFactory` (`AbruptDecayFactory.cs:57`)
  > Enforcement is deferred — see xmldoc.
- `BrainFreezeFactory` (`BrainFreezeFactory.cs:10`)
  > CR 702.40a's "you may choose new targets for the copies" rider is deferred (see <see cref="StormHelper"/> + <see cref="Majik.
- `SilvergillAdeptFactory` (`SilvergillAdeptFactory.cs:10`)
  > No reveal event is emitted in v1 (same gap as other reveal-cost cards).
- `SilvergillAdeptFactory` (`SilvergillAdeptFactory.cs:79`)
  > " v1: structural-only keyword marker; actual cost enforcement at cast-time is deferred.
- `SpellskiteFactory` (`SpellskiteFactory.cs:11`)
  > ## Deferred (v1 gaps)  - <b>Ability targets</b>: Spellskite's printed clause is "target spell or ability with a single target".
- `MeddlingMageFactory` (`MeddlingMageFactory.cs:9`)
  > <b>"nonland card name" validation</b>: the chosen name is accepted as a raw string; enforcement that it isn't a basic land name is deferred (rules-layer validation, not mechanical).
- `VampiricTutorFactory` (`VampiricTutorFactory.cs:12`)
  > The picked card moves Library → top-of-Library without publishing a reveal event; same gap as the other search factories.
- `YawgmothsWillFactory` (`YawgmothsWillFactory.cs:10`)
  > A bus-aware overload could clear it; deferred.
- `EmberethShieldbreakerFactory` (`EmberethShieldbreakerFactory.cs:12`)
  > MoveToGraveyard"/>; same gap as <see cref="SlaughterPactFactory"/> and the rest of the single-target destroy family.
- `GoblinWelderFactory` (`GoblinWelderFactory.cs:12`)
  > A formal stack / target-snapshot path is deferred to the targeting MVP.
- `MoxAmberFactory` (`MoxAmberFactory.cs:9`)
  > <b>Single modal-colour ability</b>: the engine has no "pick a colour at activation" mana-ability primitive yet — same gap as Delighted Halfling / City of Brass / Mox Opal.
- `InkmothNexusFactory` (`InkmothNexusFactory.cs:206`)
  > Keyword grants — the Infect marker is a no-op on the combat pipeline today (mechanic deferred), but Flying gates blocking legality and both will light up correctly once the Infect damage-replacement primitive lands.
- `KraulHarpoonerFactory` (`KraulHarpoonerFactory.cs:10`)
  > <b>"You may" prompt</b>: the fight is optional; deferred alongside targeting.
- `KraulHarpoonerFactory` (`KraulHarpoonerFactory.cs:60`)
  > Targeting + fight step deferred (see xmldoc above).
- `ShowAndTellFactory` (`ShowAndTellFactory.cs:10`)
  > Real "any of N choices + opt-out" prompt deferred (same queue as Stoneforge Mystic / Sun Titan).
- `MishrasWorkshopFactory` (`MishrasWorkshopFactory.cs:8`)
  > Per the same gap acknowledged across the codebase, the v1 shell ships the structural mana amount without the artifact-only gate; once a provenance ledger lands, wire the restriction here as a <c>spendableForPredicate</c>-style hook on the <c>ManaAbility</c>.
- `YawgmothFactory` (`YawgmothFactory.cs:10`)
  > Effect 4: Controller draws a card  ## Deferred (v1 gaps)
- `YawgmothFactory` (`YawgmothFactory.cs:10`)
  > <b>Discard — first non-land preference</b>: v1 picks the first card in hand regardless of card type; full oracle-compliant discard is deferred.
- `YawgmothFactory` (`YawgmothFactory.cs:82`)
  > Put a -1/-1 counter on up to one target creature (DEFERRED) 4.
- `YawgmothFactory` (`YawgmothFactory.cs:82`)
  > Draw a card  Opponent iteration deferred when opponentsResolver is null — see class xmldoc.
- `YawgmothFactory` (`YawgmothFactory.cs:142`)
  > DEFERRED — requires ITarget / TargetResolver infrastructure.
- `MutavaultFactory` (`MutavaultFactory.cs:11`)
  > ## "Every creature type" simplification (v1 gap) CR 205.3m enumerates ~250 creature subtypes; the engine's <see cref="CardSubtype"/> enum lists ~50 of them.
- `MutavaultFactory` (`MutavaultFactory.cs:11`)
  > <b>Combat math through Compute</b>: same gap as Karn's animate- artifact (<see cref="KarnAnimateArtifactEffect"/>).
- `MutavaultFactory` (`MutavaultFactory.cs:11`)
  > Mutavault was on the battlefield long enough but its Creature-ness is fresh — the intricate "had Creature type continuously since untap step" bookkeeping is deferred; the test suite asserts shape, not attack legality.
- `FetchLandCycleFactory` (`FetchLandCycleFactory.cs:11`)
  > Shuffle</c> entry point yet — same gap as every other tutor in the codebase.
- `AtraxaGrandUnifierFactory` (`AtraxaGrandUnifierFactory.cs:10`)
  > No live observer cares yet (same gap as the rest of the reveal-and-pick factories — Ancient Stirrings, Goblin Matron, Mystical Tutor).
- `CavernOfSoulsFactory` (`CavernOfSoulsFactory.cs:120`)
  > Spend-restriction ("only to cast a creature spell of the chosen type") + uncounterable rider are deferred — see class xmldoc.
- `GoblinLackeyFactory` (`GoblinLackeyFactory.cs:12`)
  > Wire a selector callback when the multi-candidate "choose a card to put onto the battlefield" prompt ships (mirrors the same gap on Stoneforge Mystic's tutor).
- `DazeFactory` (`DazeFactory.cs:11`)
  > A bot-side probe (mirror of <c>PitchAltCostProbe</c>) is deferred — Daze's pitch always pays so the probe shape is just "for each Island controlled, yield one candidate" and lives outside this factory's surface in v1.
- `PhoenixOfAshFactory` (`PhoenixOfAshFactory.cs:8`)
  > The printed "can attack as though it didn't have summoning sickness as long as it has haste" rider collapses observationally to the Haste keyword in v1 — CR 702.10b already lets a creature with haste attack the turn it came under its controller's control, so the additional clause only matters when Haste is granted-then…
- `PhoenixOfAshFactory` (`PhoenixOfAshFactory.cs:8`)
  > Distinct behaviour only manifests if Haste is removed mid-turn after the controller has owned Phoenix of Ash for less than a full turn — no keyword-removal surface yet, same gap as Goblin Chieftain's Haste-loss interactions.
- `AjaniNacatlPariahFactory` (`AjaniNacatlPariahFactory.cs:11`)
  > The MdfcState flip is the v1 observation surface — combat / loyalty interactions on the back face are deferred.
- `AjaniNacatlPariahFactory` (`AjaniNacatlPariahFactory.cs:105`)
  > The Creature object stays in place — full Layer 0 / per-face hot-swap is deferred.
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:15`)
  > ## Deferred (v1 gaps)  - <b>Prowess pump on the spawned Monk token</b>: the <c>"Prowess"</c> keyword marker is attached to the Monk token so shape inspection sees the printed reminder text, but the <see cref="Majik.
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:15`)
  > Same v1 gap as <see cref="StormchasersTalentFactory"/>'s Mercenary tokens and <see cref="MonasteryMentorFactory"/>'s Monk tokens.
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:15`)
  > <b>Sorcery-speed restriction on Equip activation (CR 702.6a)</b> — same gap as <see cref="ColossusHammerFactory"/> / <see cref="SwordOfFireAndIceFactory"/>.
- `LordOfAtlantisFactory` (`LordOfAtlantisFactory.cs:9`)
  > The combat-validator enforcement of Islandwalk ("creature can't be blocked as long as the defending player controls an Island") is deferred — same posture as Intimidate / Menace enforcement.
- `HogaakFactory` (`HogaakFactory.cs:11`)
  > ## Deferred (v1 gaps)  - <b>"This spell can't be cast unless …" gate</b>: layered as the <see cref="ExileCreaturesFromGraveyardAdditionalCost.
- `HogaakFactory` (`HogaakFactory.cs:11`)
  > <b>Convoke reduction integration</b>: same gap as Chord of Calling — <see cref="ConvokeAlternativeCost.
- `DampingSphereFactory` (`DampingSphereFactory.cs:9`)
  > ManaPaymentResolver"/> is not yet updated to plumb this through automatically — listed as a deferred gap.
- `CursecatcherFactory` (`CursecatcherFactory.cs:12`)
  > No deferred work needed here.

---

Source: `Majik.Core/CardData/MechanicDeps/`. Registry: `MechanicPrimitive.cs`.
