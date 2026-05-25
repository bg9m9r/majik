# Mechanic-dependency DAG

Scanner output: every `*Factory.cs` xmldoc / inline comment mentioning
`deferred` / `DEFERRED` / `blocked on` / `same gap`, clustered by canonical
engine primitive. Each row answers: "if we ship primitive _X_, which factory
xmldocs flagged that they're blocked on it?"

- **Generated:** 2026-05-25 01:45 UTC
- **Scanned dir:** `Majik.Core/CardData/Factories`
- **Total mentions:** 171
- **Clusters:** 6
- **Unclustered (need new registry pattern):** 125

Regenerate with `dotnet run --project Majik.Console -- mechanic-deps --md-out docs/MECHANIC_DEPS.md --json-out docs/mechanic-deps.json`.

## Priority queue

| Rank | Primitive | CR | Factories | Mentions |
|---:|---|---|---:|---:|
| 1 | Agent-prompt targeting MVP | — | 28 | 35 |
| 2 | Library shuffle (CR 701.20) | CR 701.20 | 4 | 4 |
| 3 | Layer-6 ability-grant subsystem (CR 613.1f) | CR 613.1f | 2 | 4 |
| 4 | Cast-marker on Card | — | 1 | 1 |
| 5 | Cycling-style activated-from-hand (CR 702.32 / Channel CR 702.74) | CR 702.32 | 1 | 1 |
| 6 | "Activate only as a sorcery" gate (CR 117.1a) | CR 117.1a | 1 | 1 |

## Cluster detail

### 1. Agent-prompt targeting MVP

- **Blocks:** 28 factories (35 mentions)
- **Implementation hint:** IPlayerAgent needs ChooseTarget / ChooseYesNo surfaces; many spell factories punt on real targeting prompts.

Mentions:

- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:16`)
  > Real agent-driven any-target prompt (creature / planeswalker / battle / player) is deferred behind the broader prompt surface — same posture as Lightning Bolt.
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
- `AbhorrentOculusFactory` (`AbhorrentOculusFactory.cs:13`)
  > ## Deferred (v1 gaps — small)  - <b>Agent prompt for pick-one-of-two:</b> v1 deterministically manifests the top-of-library card; the second goes to graveyard.
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
- `FaithlessLootingFactory` (`FaithlessLootingFactory.cs:111`)
  > Real agent-driven choice deferred.
- `UroTitanFactory` (`UroTitanFactory.cs:14`)
  > v1 always plays the first land in hand when one exists; a first-class yes/no agent prompt is deferred (same gap as Sun Titan / Primeval Titan / Stoneforge Mystic).
- `DauthiVoidwalkerFactory` (`DauthiVoidwalkerFactory.cs:13`)
  > Wiring an agent prompt mirrors the rest of the v1 factories (deferred).
- `PonderFactory` (`PonderFactory.cs:107`)
  > Shuffle primitive is now wired (CR 701.20), but Ponder's "may" rider is a yes/no agent prompt — deferred behind the agent-prompt MVP (rank #1 in MECHANIC_DEPS).
- `SilvergillAdeptFactory` (`SilvergillAdeptFactory.cs:10`)
  > The actual enforcement at cast-time (agent prompt: reveal a Merfolk card from hand OR pay {3} as an additional cost) is deferred until the additional-cost framework supports reveal-based alternatives.
- `GoblinPiledriverFactory` (`GoblinPiledriverFactory.cs:10`)
  > Same shape as Primeval Titan's <c>selector</c> + Plague Engineer's <c>typeChooser</c> — agent-prompt integration is deferred.
- `SwordOfFeastAndFamineFactory` (`SwordOfFeastAndFamineFactory.cs:14`)
  > the damaged player discards a card — v1 deterministically picks the first card in hand (same v1 policy as <see cref="LilianaOfTheVeilFactory"/>'s +1 each-player-discards and <see cref="FaithlessLootingFactory"/>'s last-2-in-hand; agent prompt deferred); 2.
- `SwordOfFeastAndFamineFactory` (`SwordOfFeastAndFamineFactory.cs:14`)
  > Agent-driven "you choose which card you discard" (CR 701.16a — damaged player chooses) is deferred behind the same prompt queue as Liliana of the Veil + Faithless Looting.
- `TormodsCryptFactory` (`TormodsCryptFactory.cs:11`)
  > Full agent-prompt targeting is deferred.
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
- **Blocks:** 4 factories (4 mentions)
- **Implementation hint:** Add IZone.Shuffle / ZoneService.ShuffleLibrary. Tutor-family factories all block on this single primitive.

Mentions:

- `StoneforgeMysticFactory` (`StoneforgeMysticFactory.cs:96`)
  > Reveal-event emission is deferred (see class xmldoc); CR 701.20a shuffle now wired via LibraryShuffle.
- `GoblinEngineerFactory` (`GoblinEngineerFactory.cs:108`)
  > " v1: deterministic — take the first artifact card in the library; reveal-event emission deferred (see class xmldoc); CR 701.20a shuffle is now wired via LibraryShuffle.
- `TrinketMageFactory` (`TrinketMageFactory.cs:74`)
  > Reveal-event emission is deferred (see class xmldoc); CR 701.20a shuffle now wired via LibraryShuffle.
- `PonderFactory` (`PonderFactory.cs:51`)
  > The "may shuffle" rider is deferred (no-op).

### 3. Layer-6 ability-grant subsystem (CR 613.1f)

- **CR citation:** CR 613.1f
- **Blocks:** 2 factories (4 mentions)
- **Implementation hint:** ContinuousEffectsService needs an 'ability grant' layer so 'gains <ability>' effects flow through GetAbilities() at read time.

Mentions:

- `AgathasSoulCauldronFactory` (`AgathasSoulCauldronFactory.cs:10`)
  > The layer-6 continuous effect that actually grants those abilities to battlefield creatures is deferred until the layer-6 ability-grant subsystem is in place.
- `AgathasSoulCauldronFactory` (`AgathasSoulCauldronFactory.cs:88`)
  > CR 702.49 — imprint: record this creature card on the Cauldron so the ability-grant static ability can reference it later (layer-6 grant deferred; storage wired here).
- `BloodghastFactory` (`BloodghastFactory.cs:14`)
  > A proper continuous effect (Layer 6 keyword grant gated on a live life-total predicate) is deferred until the conditional-keyword CDA surface exists.
- `BloodghastFactory` (`BloodghastFactory.cs:126`)
  > A full dynamic Layer 6 conditional keyword grant is deferred — see class xmldoc.

### 4. Cast-marker on Card

- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** Persistent 'this object was cast (vs. put onto the battlefield)' flag — Bloodghast, The One Ring, Pact triggers all key off it.

Mentions:

- `TheOneRingFactory` (`TheOneRingFactory.cs:15`)
  > The effect body is a no-op — the "if you cast it" intervening-if clause, the "until your next turn" expiry, and the "protection from everything" player-scoped grant are all deferred (no cast-marker on Card, no per-player delayed cleanup, no Player.

### 5. Cycling-style activated-from-hand (CR 702.32 / Channel CR 702.74)

- **CR citation:** CR 702.32
- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** Generic 'pay X, discard ~: <effect>' from hand. Shape covers Cycling, Channel, Forecast.

Mentions:

- `ChannelLandCycleFactory` (`ChannelLandCycleFactory.cs:12`)
  > Sokenzan, Crucible of Defiance — deferred (its Channel produces two 1/1 Spirit tokens with haste; requires a Spirit-token shape not yet in <c>TokenFactory</c>).

### 6. "Activate only as a sorcery" gate (CR 117.1a)

- **CR citation:** CR 117.1a
- **Blocks:** 1 factories (1 mentions)
- **Implementation hint:** ActionValidator-side check: ability/spell flagged sorcery-speed only legal during own main phase with empty stack.

Mentions:

- `TirelessTrackerFactory` (`TirelessTrackerFactory.cs:14`)
  > <b>"Activate only as a sorcery"</b> — Tireless Tracker's printed activated ability has NO sorcery-speed restriction (instant speed on the official card), so nothing is deferred here for this card.

## Unclustered (need new registry pattern)

- `SplinterTwinFactory` (`SplinterTwinFactory.cs:16`)
  > ## Deferred (v1 gaps)  - <b>Layer 1 copy effect</b>: the token's P/T + keywords are snapshotted at the moment the ability resolves; if the bearer's characteristics change later (counters, +1/+1 boost, lord anthems), the token does NOT track them.
- `ScapeshiftFactory` (`ScapeshiftFactory.cs:10`)
  > ## v1 gaps - <b>"Any number" prompt</b>: the engine has no first-class "pick a subset of permanents to sacrifice" agent hook.
- `OmnathLocusOfCreationFactory` (`OmnathLocusOfCreationFactory.cs:13`)
  > <b>Live "each opponent" / "each planeswalker you don't control" enumeration without resolvers</b>: same gap as Sheoldred / Meathook Massacre — <see cref="Player"/> doesn't expose opponent list at construction time.
- `BridgeFromBelowFactory` (`BridgeFromBelowFactory.cs:12`)
  > ## Deferred (v1 gaps)  - <b>APNAP simultaneous-trigger ordering</b>: when one creature dies to a chained event (combat damage, board wipe), CR 603.3b sorts pending triggers by APNAP and within each player by the player's choice.
- `TheOneRingFactory` (`TheOneRingFactory.cs:121`)
  > " Structural: "if you cast it" + "until your next turn" expiry deferred — see class xmldoc.
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:16`)
  > <b>Prowess pump on the Mercenary token</b>: still keyword-marker-only (same gap as Cori-Steel Cutter / Monastery Mentor — TokenFactory doesn't thread ContinuousEffectsService for token-resident keywords yet).
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:159`)
  > Prowess pump on token deferred — keyword marker only, see class xmldoc.
- `StormchasersTalentFactory` (`StormchasersTalentFactory.cs:319`)
  > Colors"/>; Prowess pump on the token deferred (see class xmldoc).
- `NihilSpellbombFactory` (`NihilSpellbombFactory.cs:13`)
  > Real prompt deferred until IPlayerAgent grows a ChooseYesNoAsync surface.
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
- `MonasteryMentorFactory` (`MonasteryMentorFactory.cs:160`)
  > "With prowess" on the token is deferred — see factory xmldoc.
- `MonasteryMentorFactory` (`MonasteryMentorFactory.cs:177`)
  > The Prowess keyword on the token is deferred (see class xmldoc) but the colour identity is now stamped.
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
- `GristFactory` (`GristFactory.cs:8`)
  > ## V1 simplification The "not on the battlefield" conditional is deferred.
- `GristFactory` (`GristFactory.cs:36`)
  > The oracle-text restriction is a deferred conditional layer-4 effect.
- `KarnLiberatedFactory` (`KarnLiberatedFactory.cs:140`)
  > v1 DEFERRED — shipped as a no-op so the loyalty change (and "this card is a legal -14 ability") still apply (CR 606.3).
- `AmpedRaptorFactory` (`AmpedRaptorFactory.cs:252`)
  > ) — same gap as every "cast for free from exile" hook today.
- `LurrusOfTheDreamDenFactory` (`LurrusOfTheDreamDenFactory.cs:13`)
  > The runtime "cast from outside the game" pipeline is still deferred — the engine has no sideboard zone yet (see <see cref="Majik.
- `NobleHierarchFactory` (`NobleHierarchFactory.cs:12`)
  > <b>Live combat-attackers provider</b>: same gap as Goblin Piledriver.
- `MurderousRiderFactory` (`MurderousRiderFactory.cs:12`)
  > " (LTB exile clause deferred — see Deferred section.
- `MurderousRiderFactory` (`MurderousRiderFactory.cs:12`)
  > Adding this requires the same replacement-effect surface used by the Anger of the Gods exile rider (see <see cref="AngerOfTheGodsFactory"/>); deferred to keep the v1 ship minimal.
- `PuresteelPaladinFactory` (`PuresteelPaladinFactory.cs:11`)
  > ## Deferred (v1 gaps)  - <b>"You may" prompt</b>: the ETB-draw effect is unconditional.
- `GreenSunsZenithFactory` (`GreenSunsZenithFactory.cs:14`)
  > ## Deferred (v1 gaps)  - <b>Replacing the spell's destination via the stack resolver</b>.
- `PriestOfFellRitesFactory` (`PriestOfFellRitesFactory.cs:12`)
  > </para>  ## Deferred (v1 gaps) (The activate-as-sorcery timing window is now enforced via the ActionValidator gate; see "Implemented" above.
- `PriestOfFellRitesFactory` (`PriestOfFellRitesFactory.cs:128`)
  > Guard: only fire when the Priest is currently in its owner's graveyard, so spurious activations from other zones are no-op-shaped while engine zone-scoping is deferred.
- `PriestOfFellRitesFactory` (`PriestOfFellRitesFactory.cs:148`)
  > Skip if not currently in graveyard — activation is illegal from other zones (engine gating deferred; the guard keeps shape tests honest).
- `ThroughTheBreachFactory` (`ThroughTheBreachFactory.cs:12`)
  > Through the Breach is still castable for its printed cost; the splice rider is structural-only on the oracle text and will be added when the engine has an Arcane- spell awareness pass (same gap as every other Splice card).
- `LilianaOfTheVeilFactory` (`LilianaOfTheVeilFactory.cs:118`)
  > v1 deferred — loyalty change applies with an empty body so the cost is still paid.
- `ReflectorMageFactory` (`ReflectorMageFactory.cs:149`)
  > DEFERRED: "That creature's owner can't cast spells with the same name as that creature until your next turn.
- `WrennsResolveFactory` (`WrennsResolveFactory.cs:11`)
  > Multi-player turn-skipping nuances deferred.
- `ChannelLandCycleFactory` (`ChannelLandCycleFactory.cs:217`)
  > Combat-state gating is deferred — v1 destroys any creature passed in.
- `WallOfRootsFactory` (`WallOfRootsFactory.cs:11`)
  > No deferred work; behaviour matches the printed card.
- `SylvanScryingFactory` (`SylvanScryingFactory.cs:9`)
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
- `MysticalTutorFactory` (`MysticalTutorFactory.cs:14`)
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
- `UroTitanFactory` (`UroTitanFactory.cs:14`)
  > ("Elder" creature subtype is not yet in <see cref="CardSubtype"/> — Giant is wired; Elder is deferred — see gaps below.
- `UroTitanFactory` (`UroTitanFactory.cs:14`)
  > <see cref="CardSubtype"/> only carries Giant; Elder is not yet in the enum, mirroring the same gap for other "Elder X" creatures (Elder Dragons etc).
- `BloodghastFactory` (`BloodghastFactory.cs:14`)
  > "You may" prompt: auto-accepted (same gap as Arclight Phoenix / Sneak Attack / Tireless Tracker).
- `KarnScionOfUrzaFactory` (`KarnScionOfUrzaFactory.cs:12`)
  > <b>-1</b>: DEFERRED to a no-op body (loyalty change still applies per CR 606.3).
- `KarnScionOfUrzaFactory` (`KarnScionOfUrzaFactory.cs:12`)
  > TargetRequest"/>s yet, so the opponent picker for the +1 isn't agent-driven (same gap as <see cref="WrennAndRealmbreakerFactory"/>).
- `KarnScionOfUrzaFactory` (`KarnScionOfUrzaFactory.cs:164`)
  > DEFERRED — requires "exiled with this source" tag tracking on exiled cards.
- `NecropotenceFactory` (`NecropotenceFactory.cs:14`)
  > Auto-cleanup mirrors the <see cref="DauthiVoidwalkerFactory"/> v1 gap.
- `NecropotenceFactory` (`NecropotenceFactory.cs:163`)
  > Face-down exile is deferred — engine has no face-down flag.
- `MishrasBaubleFactory` (`MishrasBaubleFactory.cs:12`)
  > Multi-player turn-skipping semantics deferred.
- `DauthiVoidwalkerFactory` (`DauthiVoidwalkerFactory.cs:13`)
  > Cleanup) and is deferred until a cast-permission flag is added.
- `DauthiVoidwalkerFactory` (`DauthiVoidwalkerFactory.cs:117`)
  > Unregister{TIntent}"/> when Voidwalker leaves the battlefield (v1 — automatic leave-cleanup deferred).
- `DauthiVoidwalkerFactory` (`DauthiVoidwalkerFactory.cs:140`)
  > Combat ability: can block / be blocked only by creatures with Shadow.
- `GoblinMatronFactory` (`GoblinMatronFactory.cs:12`)
  > The picked card moves Library → Hand without publishing a CardRevealedEvent; same gap as the other tutor factories.
- `TasigurTheGoldenFangFactory` (`TasigurTheGoldenFangFactory.cs:11`)
  > ## Deferred (v1 gaps) (The activate-as-sorcery timing window for the {B}{G}{U} ability is now enforced via the ActionValidator gate; see "Implemented" above.
- `AbruptDecayFactory` (`AbruptDecayFactory.cs:12`)
  > <b>Can't be countered</b> — a <see cref="KeywordAbility"/> marker "Can't Be Countered" is attached to the card shape (structural; actual enforcement via SpellCaster / StackResolver is deferred — same posture as Veil of Summer's turn-scoped uncounterable rider and Force of Will's text interaction).
- `AbruptDecayFactory` (`AbruptDecayFactory.cs:59`)
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
- `VampiricTutorFactory` (`VampiricTutorFactory.cs:13`)
  > The picked card moves Library → top-of-Library without publishing a reveal event; same gap as the other search factories.
- `YawgmothsWillFactory` (`YawgmothsWillFactory.cs:10`)
  > A bus-aware overload could clear it; deferred.
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
- `AtraxaGrandUnifierFactory` (`AtraxaGrandUnifierFactory.cs:10`)
  > No live observer cares yet (same gap as the rest of the reveal-and-pick factories — Ancient Stirrings, Goblin Matron, Mystical Tutor).
- `CavernOfSoulsFactory` (`CavernOfSoulsFactory.cs:120`)
  > Spend-restriction ("only to cast a creature spell of the chosen type") + uncounterable rider are deferred — see class xmldoc.
- `GoblinLackeyFactory` (`GoblinLackeyFactory.cs:12`)
  > Wire a selector callback when the multi-candidate "choose a card to put onto the battlefield" prompt ships (mirrors the same gap on Stoneforge Mystic's tutor).
- `DazeFactory` (`DazeFactory.cs:11`)
  > A bot-side probe (mirror of <c>PitchAltCostProbe</c>) is deferred — Daze's pitch always pays so the probe shape is just "for each Island controlled, yield one candidate" and lives outside this factory's surface in v1.
- `AjaniNacatlPariahFactory` (`AjaniNacatlPariahFactory.cs:11`)
  > The MdfcState flip is the v1 observation surface — combat / loyalty interactions on the back face are deferred.
- `AjaniNacatlPariahFactory` (`AjaniNacatlPariahFactory.cs:105`)
  > The Creature object stays in place — full Layer 0 / per-face hot-swap is deferred.
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:15`)
  > ## Deferred (v1 gaps)  - <b>Prowess pump on the spawned Monk token</b>: the <c>"Prowess"</c> keyword marker is attached to the Monk token so shape inspection sees the printed reminder text, but the <see cref="Majik.
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:15`)
  > Same v1 gap as <see cref="StormchasersTalentFactory"/>'s Mercenary tokens and <see cref="MonasteryMentorFactory"/>'s Monk tokens.
- `CoriSteelCutterFactory` (`CoriSteelCutterFactory.cs:179`)
  > Prowess pump on the token is deferred — see class xmldoc.
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
