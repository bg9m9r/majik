# Modern Coverage

Living tracker for Modern-format card + mechanic implementation in the Majik engine.

**Last updated:** 2026-05-23
**Latest origin/main:** Vampiric Tutor (Instant {B} — search library for ANY card → top of library + lose 2 life; sibling of Mystical Tutor with no type predicate and an unconditional 2-life payment via `Player.LoseLife`; shuffle deferred — same rationale as the rest of the tutor surface) on top of Tendrils of Agony (Sorcery {2}{B}{B} — target opponent loses 2 life + you gain 2 life + Storm trigger (CR 702.40) reusing `StormHelper`; second storm spell after Brain Freeze, sharing the same copy-via-`SpellCopier` v1 semantics) on top of Pernicious Deed (Enchantment {1}{B}{G} — {X}, Sacrifice: destroy each artifact, creature, and enchantment with mv ≤ X across all battlefields; mirrors Engineered Explosives' v1 X-provider + sacrifice-stub pattern) on top of Dragon's Rage Channeler + Wheel of Fortune + Brain Freeze + Lightning Helix + Sword of Feast and Famine + Boros Reckoner + Mishra's Workshop + Goblin Welder + Sword of Fire and Ice + Reckless Charge + Trinket Mage + Sun Titan + Sensei's Divining Top + Inkmoth Nexus + Spell Queller + Goblin Matron + Mutavault + Skullclamp + Umezawa's Jitte + Wasteland + Swords to Plowshares + Mystical Tutor + Path to Exile + Daze + Ponder + Preordain + Splinter Twin + Sythis, Harvest's Hand + Pyromancer's Goggles + Plague Engineer + Manabarbs + Yawgmoth's Will + Wishclaw Talisman + Searing Blaze + Goblin Lackey + Damping Sphere.

## Headline numbers

| Metric | Count |
|---|---|
| Named factories | 138 |
| Bespoke templates | 27 |
| Generic templates | 94 |
| JSON-defined cards | 15 |
| Seeded cards | 60 |
| Estimated Modern meta coverage | ~15% |

(Coverage estimate is rough: counts top-25 archetype staples present in the engine vs. total. Many ancillary archetype pieces remain unimplemented.)

## Implemented cards

### Named factories (alphabetical)

One row per file under `Majik.Core/CardData/Factories/`. PR column is the most recent PR that meaningfully touched the factory (from git log subjects).

| Card | Type | PR | Note |
|---|---|---|---|
| Aether Gust | Instant | TBD | {1}{U} put target red/green spell or permanent on top or bottom of its owner's library (top/bottom decision via optional chooser callback; defaults to bottom) |
| Aether Vial | Artifact | TBD | upkeep charge counter + {T} put creature from hand with mv = counters |
| Agatha's Soul Cauldron | Artifact | — | activated counter-share |
| Amulet of Vigor | Artifact | TBD | untap-on-enters-tapped trigger |
| Ancient Stirrings | Sorcery | #201 | top-5 colorless reveal + random-bottom |
| Badgermole Cub | Creature | — | earthbend shell |
| Blood Moon | Enchantment | #156 | nonbasic-to-Mountain Layer 4 |
| Bonecrusher Giant | Creature | TBD | 4/3 Giant {2}{R} + targeted-by-spell trigger deals 2 to spell's controller (Adventure / Stomp half deferred) |
| Boros Reckoner | Creature | TBD | 3/3 Minotaur Wizard {R/W}{R/W}{R/W} + first strike + damage-received trigger redirecting that much damage to any target (v1 triggered, not the printed *replacement* effect — damage still resolves on Boros Reckoner before the redirect fires, and the redirect goes on the stack). Hybrid {R/W} parses via ManaCost.Parse's HybridPip path |
| Boseiju, Who Endures | Land | — | channel destroy stub |
| Brain Freeze | Instant | TBD | {U}{U} — mill 3 target player + Storm (CR 702.40) on-cast trigger via StormHelper (count = TurnState.SpellsCastByPlayer - 1; copies via SpellCopier.PushCopyOfTopSpell re-executing the spell's effects per copy). CR 702.40a "you may choose new targets" + copies-as-distinct-stack-objects deferred (inherited from SpellCopier) |
| Cabal Ritual | Instant | TBD | add {B}{B}{B}; threshold (7+ own grave) replaces with five colourless (CR 702.50) |
| Cavern of Souls | Land | TBD | ETB choose-creature-type + {T}: {C} + {T}: any color (spend-restriction + uncounterable rider deferred) |
| Chalice of the Void | Artifact | TBD | {X}{X} — ETB with X charge counters (via PendingCastX) + symmetric "counter spell of MV = counters" trigger |
| Chord of Calling | Instant | TBD | Flash + Convoke + X tutor creature mv ≤ X → battlefield (convoke reduction integration deferred) |
| Colossus Hammer | Artifact | TBD | Equipment {1}: +10/+0 + lose flying + equip {8} |
| Conversion | Enchantment | #157 | Mountains-are-Plains retype |
| Consider | Instant | — | surveil 1 + draw |
| Crashing Footfalls | Sorcery | TBD | cascade trigger + 2x 4/4 Rhino warrior tokens with trample |
| Cryptic Command | Instant | #191 | modal choose-2 |
| Damping Sphere | Artifact | #257 | {2} — symmetric land-mana cap to {C} on ≥2-mana taps (DampingSphereCappedManaAbility) + per-spell +{1} for each prior spell this turn (SpellCostIncreaseAbility scanned via CostReduction.GetEffectiveCost). Live-wiring of ManaPaymentResolver / SpellCastFlow / TurnDriver deferred — semantics locked via helpers + tests |
| Dark Confidant | Creature | #178 | upkeep reveal + life loss |
| Daze | Instant | TBD | bounce-Island pitch alt cost + counter target spell unless its controller pays {1} (CR 118.9 / 118.4) |
| Dauthi Voidwalker | Creature | TBD | Shadow + opponent-grave→exile-with-void-counter replacement + {2},{T},remove counter: cast-for-free from exile |
| Death's Shadow | Creature | TBD | Layer 7a CDA P/T scaled by controller life |
| Dragon's Rage Channeler | Creature | TBD | {R} 1/1 Human Shaman + noncreature-cast surveil 1 trigger + delirium static (CR 702.105) registering Layer 7c +2/+2 and Layer 6 Flying grants when controller's graveyard has 4+ distinct card types (live sample via TarmogoyfFactory.CountDistinctCardTypes) |
| Delighted Halfling | Creature | — | any-color mana ability |
| Dig Through Time | Sorcery | #181 | delve top-7 to hand |
| Dredger's Insight | Enchantment | — | dies-trigger surveil-equivalent |
| Dress Down | Enchantment | #195 | lose-abilities + 1/1 base PT |
| Dryad Arbor | Land Creature | — | 1/1 Forest creature, no cost |
| Eldritch Evolution | Sorcery | TBD | sac-creature additional cost + tutor creature mv ≤ sac.mv+2 → battlefield + self-exile (CR 601.2f / 701.19a / 608.2) |
| Elegant Parlor | Land | — | R/W surveil dual |
| Endurance | Creature | TBD | MH2 incarnation: Flash + Reach + evoke pitch + ETB shuffle-graveyard-to-library |
| Engineered Explosives | Artifact | TBD | {X} Sunburst (charge counters via v1 X-provider) + {2}, sac: destroy each nonland permanent with mv = counters |
| Faithless Looting | Sorcery | TBD | draw 2 + discard 2 + flashback {2}{R} (alt cost parsed from oracle via FlashbackOracleParser) |
| Fiery Islet | Land | — | pay-1-life U/R + sac-draw |
| Force of Negation | Instant | #185 | pitch counter (non-creature) |
| Force of Will | Instant | #185 | pitch counter (universal) |
| Fury | Creature | — | evoke pitch + ETB X-damage divided |
| Galvanic Discharge | Instant | TBD | 1 + charge counters on artifacts/lands you control damage |
| Goblin Bombardment | Enchantment | — | sac-creature → 1 damage |
| Goblin Lackey | Creature | TBD | {R} 1/1 Goblin + combat-damage-to-player trigger cheats first Goblin creature card from hand onto the battlefield (ZoneService-routed for ETB triggers) |
| Goblin Matron | Creature | TBD | {2}{R} 1/1 Goblin + ETB tutor a Goblin card from library to hand (agent-driven pick with deterministic first-match fallback; shuffle deferred) |
| Goblin Welder | Creature | TBD | {R} 1/1 Goblin Artificer + {T} activated ability: pair same-player (battlefield artifact, graveyard artifact card) and sac-then-reanimate (ZoneService-routed when supplied; raw zone fallback for shape tests). Target prompt + on-stack legality check deferred — resolution scans candidate players in order and picks the first legal pair |
| Grief | Creature | #205 | evoke pitch + ETB discard |
| Grist, the Hunger Tide | Planeswalker | — | +1 token, -2 reanimate |
| Harbinger of the Seas | Creature | #157 | nonbasic-to-Island |
| Inkmoth Nexus | Land | — | {T}: {C} + {1}: until EOT becomes 1/1 Phyrexian Insect artifact creature with flying + infect (still a land); infect mechanic deferred — keyword marker only |
| Inspiring Vantage | Land | — | R/W fastland |
| Karn, the Great Creator | Planeswalker | TBD | opponent-artifact static + +1 animate + -2 wishboard |
| Karn Liberated | Planeswalker | TBD | +4 exile-from-hand, -3 exile-permanent; -14 restart deferred |
| Karakas | Land | TBD | {T}: {W} + {T}: bounce target legendary creature (Legendary) |
| Kraul Harpooner | Creature | — | fight-flyer shell |
| Lazotep Recruit | Creature | — | amass-keyword shell |
| Ledger Shredder | Creature | #193 | second-spell surveil + counter |
| Library Surveyor | Creature | — | ETB tutor shell |
| Lightning Helix | Instant | TBD | {R}{W} — 3 damage to any target (Player / Creature / Planeswalker via shared `SearingBlazeFactory.DealDamageWithPlaneswalker`) + controller gains 3 life; single any-target request |
| Liliana of the Veil | Planeswalker | #178 | +1 discard, -2 discard |
| Living End | Sorcery | TBD | Cascade + each-player mass-exile-grave + sac-creatures + mass-reanimate |
| Lurrus of the Dream-Den | Creature | TBD | Lifelink + cast-permanent-mv≤2-from-graveyard once per your turn (companion deck-rule deferred) |
| Magus of the Moon | Creature | #157 | nonbasic-to-Mountain |
| Manabarbs | Enchantment | TBD | {2}{R}{R} — symmetric "whenever a player taps a land for mana, deal 1" triggered ability over ManaAbilityActivatedEvent (CR 605); source gate matches Land via printed type; Mox Opal / Black Lotus / other non-land mana abilities publish the same event but are rejected by the source predicate |
| Manamorphose | Instant | TBD | {1}{R/G} hybrid + add two mana any combo (caller-picked pair, default {R}{G}) + cantrip |
| Mishra's Bauble | Artifact | — | sac → look + delayed draw |
| Mishra's Workshop | Land | TBD | {T}: Add {C}{C}{C}; artifact-only spend restriction structural-only — provenance ledger deferred (CR 106.4) |
| Mox Opal | Artifact | TBD | Legendary {0}: Metalcraft-gated any-color mana (CR 702.95) |
| Murktide Regent | Creature | #194 | delve cost + ETB X counters |
| Mutavault | Land | TBD | {T}: Add {C} + {1}: until EOT becomes 2/2 every-creature-type creature, still a land (Layer 4 add-Creature + every-modelled-creature-subtype + Layer 7b set-base PT 2/2, both ExpireAtEndOfTurn; non-Creature runtime instance — PT recorded as shim until Compute(Permanent) upgrades chars row) |
| Mystical Tutor | Instant | TBD | {U} — search library for instant/sorcery, reveal, top of library (shuffle deferred) |
| Necropotence | Enchantment | TBD | {B}{B}{B} — skip-draw via SkipDrawRegistry + discard→exile ZoneMoveIntent replacement + Pay 1 life: exile top of library + delayed end-step return-to-hand (face-down exile deferred) |
| Orcish Bowmasters | Creature | — | reactive ping shell |
| Pact of Negation | Instant | TBD | {0} — counter target spell + delayed upkeep DelayedTriggeredAbility (CR 603.7) that tries PayMana({3}{U}{U}); on failure MarkLost() (CR 104.3 / 118.3); upkeep agent prompt deferred |
| Path to Exile | Instant | TBD | {W} — exile target creature + exiled creature's controller may tutor basic land tapped (CR 701.21 + CR 701.19a); library shuffle deferred (no IZone.Shuffle) |
| Pernicious Deed | Enchantment | TBD | {1}{B}{G} — {X}, Sacrifice: destroy each artifact, creature, and enchantment with mv ≤ X across all battlefields; mirrors Engineered Explosives' v1 shape (ManaCostCost {X} + AdditionalCost.Sacrifice + caller-supplied X provider + allPlayersResolver). Sacrifice payment stub at AdditionalCost.Pay; effect closure performs the zone move (CR 701.16) |
| Phantasmal Image | Creature | TBD | 0/0 Illusion {1}{U} + EntersAsCopyReplacement (AnyBattlefield) + Layer 4 Illusion subtype rider + targeted-by-spell-or-ability self-sacrifice trigger |
| Pithing Needle | Artifact | #189 | name-targeted activated suppression |
| Plague Engineer | Creature | TBD | 2/2 Human Rogue {2}{B} + Deathtouch + ETB choose-creature-type + Layer 7c -1/-1 to opponents' creatures of chosen type (LordStaticEffect opponentsOnly) |
| Phyrexian Tower | Land | — | {T}: {C} + {T}, sac creature: {B}{B} (Legendary) |
| Priest of Fell Rites | Creature | #196 | ETB reanimate + grave-unearth |
| Primeval Titan | Creature | TBD | Trample + ETB/attack tutor up to 2 lands tapped |
| Puresteel Paladin | Creature | TBD | Equipment-ETB draw trigger + zero-equip-cost on ≥3 artifacts |
| Pyromancer's Goggles | Artifact | TBD | Legendary {5} — {T}: Add {R} + structural copy-on-cast trigger (Instant/Sorcery, controller-match); mana-provenance gate + stack-copy primitive + new-targets prompt deferred |
| Ragavan, Nimble Pilferer | Creature | TBD | combat-damage Treasure + exile + may-cast EOT (CR 118.9 grant + ExileCastAlternativeCost); Dash deferred |
| Reckless Charge | Sorcery | TBD | {R} — target creature gets +3/+0 and gains haste until end of turn; Flashback {2}{R} (alt cost parsed from oracle via FlashbackOracleParser, mirroring Faithless Looting) |
| Rift Bolt | Sorcery | #183 | suspend → 3 damage |
| Scavenging Ooze | Creature | #188 | exile-graveyard + counter + life |
| Scion of Draco | Artifact Creature | TBD | Domain cost-reduction {10} → {0} at 5 basic types (CR 702.16); keyword-grant rider deferred |
| Sea's Claim | Aura | #160 | enchanted land becomes Island |
| Searing Blaze | Instant | TBD | 1 damage to player/planeswalker + 1 to a creature they control; landfall → 3 each instead (resolution-time TurnState.LandEnteredThisTurn gate) |
| Sensei's Divining Top | Artifact | TBD | {1} artifact with two activated abilities — {T}: peek top 3 + agent-driven reorder via ScryAction with ToBottom=[] (mirrors Ponder; default preserves order); {1}, {T}: draw a card (empty-library flags MarkTriedToDrawFromEmptyLibrary per CR 704.5b) then move Top from battlefield to library index 0 via IZone.InsertCardAt. Printed Legendary supertype omitted in v1. |
| Sigarda's Aid | Enchantment | TBD | flash-grant equipment/aura + ETB auto-attach |
| Skullclamp | Artifact | TBD | Equipment {1} — AttachedBoostEffect(+1, -1) at Layer 7c; dies trigger (CR 603.6c) matches the currently-equipped creature's Battlefield→Graveyard CardMovedEvent and draws 2 cards; Equip {1}. Sorcery-speed gate + attach-target prompt deferred (same as Colossus Hammer) |
| Snapcaster Mage | Creature | #170 | flash + ETB flashback grant |
| Solitude | Creature | — | evoke pitch + ETB exile |
| Spell Queller | Creature | TBD | Spirit {1}{W}{U} 2/3 Flash — ETB exile target spell with mv ≤ 4 (Snapcaster-style target stamping); LTB releases the exiled card so its owner may free-cast via CastFromExileAlternativeCost (host-callback shape, Cascade-style) |
| Spell Snare | Instant | TBD | counter target spell with mana value 2 (resolution-time MV sample, CR 202.3 / 608.2b) |
| Splinter Twin | Aura | TBD | {2}{R}{R} Aura — grant-activated-ability-on-attach via AttachedAuraAbilityGrantStaticEffect; bearer gains `{T}: create haste token copy + exile EOT` while attached; revoked on aura LTB. v1 token snapshots bearer name/printed P/T/subtypes/keywords at activation (no live CopyEffect). Delayed end-step exile registered as a DelayedTriggeredAbility when a TriggerManager is wired |
| Spreading Seas | Aura | #160 | retype land + draw |
| Spymaster's Vault | Land | — | B-source shell |
| Stoneforge Mystic | Creature | #184 | ETB tutor + activated put |
| Stony Silence | Enchantment | TBD | global artifact-activated suppression (mana exempt; CR 605) |
| Stubborn Denial | Instant | — | ferocious-conditional counter |
| Subtlety | Creature | TBD | evoke pitch + ETB bounce + look-and-bottom |
| Sun Titan | Creature | TBD | {4}{W}{W} 6/6 Giant — Vigilance + ETB/attack reanimate target permanent card with mv ≤ 3 from controller's graveyard to battlefield. ReanimatePermanentPick scans `Permanent` (any artifact/creature/enchantment/land/planeswalker — CR 110.4) and routes graveyard→battlefield through ZoneService when supplied so ETB triggers on the reanimated permanent fire (CR 603.6a). Deterministic first-match v1 ("you may" + target prompt deferred, same as Priest of Fell Rites) |
| Sunbaked Canyon | Land | — | pay-1-life R/W + sac-draw |
| Surgical Extraction | Instant | #192 | phyrexian global name exile |
| Sword of Feast and Famine | Artifact | TBD | Equipment {3} — `AttachedBoostEffect(+2,+2)` at Layer 7c + two `AttachedAuraAbilityGrantStaticEffect` lifecycles granting `ProtectionAbility("black")` + `ProtectionAbility("green")` on attach (revoked on detach/LTB) so the equipped creature feeds CR 702.16 via `Rules.Protection.HasProtectionFromColor`; combat-damage-to-a-player trigger has the damaged player discard a card (v1 first-card-in-hand pick) + untaps every `Land` the Sword's controller controls (CR 510 / 603.1 / 701.16a / 701.20); Equip {2}. Sorcery-speed gate + attach-target prompt + discard-prompt deferred (same queue as Colossus Hammer / Liliana of the Veil) |
| Sword of Fire and Ice | Artifact | TBD | Equipment {3} — AttachedBoostEffect(+2, +2) at Layer 7c; ProtectionAbility("red") + ProtectionAbility("blue") markers on the equipment card (full DEBT-A enforcement deferred — no attachment-aware Layer 6 grant for protection yet); combat-damage-to-a-player trigger (CR 510) deals 2 damage to any target via OracleSpellBinder.DealDamage + draws 1 (damage half no-ops without a chosen target; paired draw still resolves per CR 608.2b); Equip {2}. Sorcery-speed gate + attach-target prompt deferred (same as Colossus Hammer / Skullclamp / Jitte) |
| Swords to Plowshares | Instant | TBD | Instant {W} — exile target creature; its controller gains life equal to its (live Compute) power; power floored at zero |
| Sylvan Scrying | Sorcery | TBD | any-land tutor to hand (Tron enabler) |
| Sythis, Harvest's Hand | Creature | TBD | Legendary Nymph 1/2 {G}{W} — Constellation: enchantment-ETB-under-controller → gain 1 life + draw 1 (covers plain enchantments AND Auras via CardType.Enchantment predicate) |
| Tarmogoyf | Creature | #173 | CDA P/T from grave types |
| Teferi, Time Raveler | Planeswalker | #182 | sorcery-speed restriction emblem |
| Tendrils of Agony | Sorcery | TBD | {2}{B}{B} — target opponent loses 2 life and you gain 2 life + Storm (CR 702.40) on-cast trigger via StormHelper (count = TurnState.SpellsCastByPlayer - 1; copies via SpellCopier.PushCopyOfTopSpell re-executing the spell's effects per copy → N-spell storm cast = (N) × 2-life swing). CR 702.40a "you may choose new targets" + copies-as-distinct-stack-objects deferred (inherited from SpellCopier) |
| Test Conniver | Creature | — | connive-keyword test card |
| Thundering Falls | Land | — | U/R surveil dual |
| Tireless Tracker | Creature | TBD | Human Scout 3/2: landfall-style Clue trigger + {2}, sac Clue: +1/+1 counter |
| Torpor Orb | Artifact | — | ETB-trigger suppression |
| Treasure Cruise | Sorcery | #181 | delve draw 3 |
| Tribal Flames | Sorcery | TBD | Domain X damage = distinct basic land types you control (CR 702.16) |
| Trinket Mage | Creature | TBD | Human Wizard {2}{U} 2/2 — ETB tutor: search library for an artifact card with mana value 1 or less → reveal → hand (deterministic first-match by `c.HasType(Artifact) && c.ManaCostValue.TotalValue <= 1`); shuffle + reveal-event deferred (same as Stoneforge Mystic) |
| Umezawa's Jitte | Artifact | TBD | Legendary Equipment {2} — combat-damage trigger by equipped creature (CR 510) adds 2 charge counters; three modal activated abilities, each paid by new RemoveChargeCounterCost: (1) 2 damage to any target via OracleSpellBinder.DealDamage, (2) -1/-1 EOT via PumpUntilEndOfTurnEffect, (3) you gain 2 life; Equip {2}. Modes fanned out into separate abilities — native modal-activated infra deferred |
| Underground Mortuary | Land | — | U/B surveil dual |
| Unholy Heat | Instant | #190 | delirium variable damage |
| Up the Beanstalk | Enchantment | TBD | ETB draw + cast-MV-5+ draw |
| Urborg, Tomb of Yawgmoth | Land | #158 | grant Swamp to all lands |
| Urza's Mine | Land | TBD | Tron — {T}: {C}, {2} if all 3 Urza lands controlled |
| Urza's Power-Plant | Land | TBD | Tron — {T}: {C}, {2} if all 3 Urza lands controlled |
| Urza's Tower | Land | TBD | Tron — {T}: {C}, {2} if all 3 Urza lands controlled |
| Vampiric Tutor | Instant | TBD | {B} — search library for any card → top of library + controller loses 2 life. Sibling of Mystical Tutor: no predicate (unrestricted pick), pick destination is library index 0 via IZone.InsertCardAt, deterministic first-match fallback when no agent is registered. CR 701.19a decline path supported (agent returns null → no tutor); 2-life payment fires unconditionally via Player.LoseLife. Shuffle deferred (no IZone.Shuffle — same rationale as the rest of the tutor surface) |
| Veil of Summer | Instant | TBD | conditional draw on opp UB cast + uncounterable rider (structural) + Hexproof-from-Blue/Black grant on controller's creatures (structural — TargetLegality only checks bare Hexproof; player-side hexproof deferred) |
| Vexing Bauble | Artifact | — | sac-draw shell |
| Walking Ballista | Artifact Creature | — | grow + ping |
| Wasteland | Land | TBD | {T}: {C} + {T}, sac: destroy target nonbasic land (ActivatedAbility with TargetRequest; self-sacrifice inline at resolution while AdditionalCost.Sacrifice is stubbed — mirrors Karakas's bounce shape) |
| Wastewood Verge | Land | — | B/G activation-gate land |
| Wheel of Fortune | Sorcery | TBD | {2}{R} — each player discards their hand, then draws seven cards. All discards resolve before any draws (the "then" sequences both halves); empty-library mid-draw flags CR 704.5b SBA loss on that player only. Card shape only at the named-factory dispatcher; resolve effect (hand → graveyard for every player, then 7 top-of-library draws) is built on demand via `WheelOfFortuneFactory.BuildResolveEffect(allPlayers)`. Distinct from the shuffle-wheel template (Day's Undoing / Time Reversal / Echo of Eons / Emergency Powers) which routes through `SpellTemplates.Templates.Library.WheelTemplate` — Wheel of Fortune discards into graveyard rather than shuffling hand+graveyard into library |
| Wishclaw Talisman | Artifact | TBD | {1}{B} ETB tapped + {T}, Pay 3 life: tutor any card → hand + ControlChangeEffect (CR 613.2) swaps control to a caller-chosen opponent; sorcery-speed gate + shuffle + opponent-prompt deferred |
| Wrenn and Six | Planeswalker | #178 | +1 land return, -1 ping |
| Wrenn and Realmbreaker | Planeswalker | TBD | +1 mill 3 + may-return-land, -2 reanimate nonland permanent, -7 structural emblem |
| Wrenn's Resolve | Sorcery | TBD | Draw 2 + delayed end-step exile rider on drawn cards still in hand |
| Wurmcoil Engine | Artifact Creature | TBD | deathtouch + lifelink + dies-trigger twin tokens |
| Yavimaya, Cradle of Growth | Land | #158 | grant Forest to all lands |
| Yawgmoth, Thran Physician | Creature | — | pay life + sac → discard/draw |
| Yawgmoth's Will | Sorcery | TBD | {2}{B} — until EOT, play cards from your graveyard (stamp Card.RuntimeGraveyardCastCost on every card in controller's graveyard) + EOT-expirable controller-grave→exile ZoneMoveIntent replacement (CR 614) |

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
- Inquisition of Kozilek — `Bespoke/InquisitionOfKozilekPatternTemplate` (nonland + mv ≤ 3 cap; no life cost)
- Expressive Iteration — `Bespoke/ExpressiveIterationTemplate`
- Brainstorm — `Bespoke/BrainstormTemplate` (draw 3, put 2 from hand on top of library; v1 deterministic return order)
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
| Storm | Done (TBD) | `Keywords/StormHelper.cs` | Brain Freeze |
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

- **Burn** — Strong. Lightning Bolt, Lava Spike, Lava Dart, Skewer the Critics, Boros Charm, Eidolon of the Great Revel, Goblin Guide, Monastery Swiftspear, Rift Bolt, Searing Blaze, Lightning Helix all in. Missing: Roiling Vortex, Sunscorched Desert. ~85%.
- **Death's Shadow** — Mid-high. Thoughtseize, Fatal Push, Snapcaster Mage, Stubborn Denial, Death's Shadow itself (CDA P/T scaled by controller life — Layer 7a) all in. Mishra's Bauble in. Temur Battle Rage absent. ~60%.
- **Murktide / Izzet Tempo** — High. Murktide Regent done, Counterspell done, Snapcaster Mage done, Lightning Bolt done, Expressive Iteration done, Ledger Shredder done, Consider done, Spell Pierce done, Subtlety done. Missing: Demilich absent. ~75%.
- **Mono-Green Tron** — High. Ancient Stirrings, Sylvan Scrying, Wurmcoil Engine done. Karn Liberated done. Karn, the Great Creator done. Tron lands (Urza's Mine + Tower + Power-Plant) done with the conditional {2} mana ability. ~70%.
- **Living End / Crashing Footfalls cascade** — High. Cascade keyword done (`Keywords/CascadeAction.cs`) + Crashing Footfalls shipped (#219). Living End shipped (this PR) with both the Cascade trigger and the resolve chain (per-player mass-exile-grave + sacrifice-creatures + mass-reanimate; ETB triggers fire on reanimated permanents via PR #174 plumbing). Suspend itself is done (#183). ~75%.
- **Rakdos Scam** — High. Grief done (#205, mirrors Solitude evoke + ETB pattern). Fury done (mirrors Solitude/Grief). Ragavan, Nimble Pilferer done (combat-damage Treasure + exile + may-cast EOT grant; Dash deferred). Dauthi Voidwalker done (Shadow + opponent-grave→exile-with-void-counter replacement effect + {2},{T},remove-void-counter activated cast-from-exile via CastFromExileAlternativeCost; EOT "this turn" timing on the cast permission deferred). Liliana of the Veil done, Fatal Push done, Thoughtseize done. ~75%.
- **Yawgmoth combo** — High. Yawgmoth done. Yawgmoth's Will done (Sorcery {2}{B} — stamps Card.RuntimeGraveyardCastCost on every card in controller's graveyard + EOT-expirable grave→exile ZoneMoveIntent replacement; covers the "play cards from your graveyard" + "exile instead of graveyard" oracle clauses). Undying creatures (Young Wolf, Strangleroot Geist, Geralf's Messenger) done. Chord of Calling done. Eldritch Evolution done — tutor-with-sac is a primary engine starter for the deck. ~75%.
- **Domain Zoo** — Mid. Boros Charm done, fetches done, shocks done, Tribal Flames done, Scion of Draco's domain cost-reduction done (keyword-grant rider deferred). Territorial Kavu absent. ~45%.
- **Amulet Titan** — Mid. Amulet of Vigor done (untap-on-enters-tapped trigger) + Primeval Titan done (ETB + attack land-tutor for up to 2, tapped). No bounce lands. ~30%.
- **Lurrus Companion** — Low-mid. Lurrus of the Dream-Den done (Lifelink + once-per-turn cast-permanent-mv≤2-from-graveyard; companion deck-construction rule deferred). Pairs with the existing low-mv permanent suite (Mishra's Bauble, Dryad Arbor, Stoneforge Mystic, Walking Ballista, Dark Confidant, etc.). Deck-construction enforcement absent. ~30%.
- **Hammer Time / Equipment** — Mid-high. Stoneforge Mystic done. Colossus Hammer done (+10/+0 + lose flying via AttachedBoostEffect + new LoseKeywordEffect). Sigarda's Aid done (flash-grant on Equipment/Aura via FlashGrantRegistry + ETB-attach rider). Puresteel Paladin done (Equipment-ETB draw trigger + zero-equip-cost lifecycle binder that gates on ≥3 artifacts; equip-cost consumer wires up when an `EquipActivatedAbility` primitive lands). Sword of Fire and Ice done (markers-on-the-card path; DEBT-A grant deferred). Sword of Feast and Famine done (lifecycle-attached `ProtectionAbility("black")` + `ProtectionAbility("green")` via two `AttachedAuraAbilityGrantStaticEffect` so the equipped creature surfaces protection through `Rules.Protection.HasProtectionFromColor` — closes the grant-on-attach gap for this Sword; combat trigger has damaged player discard + untaps controller's lands). ~55%.
- **Merfolk** — Low. Aether Vial done (mana-free creature-cheater that's the deck's engine). Spreading Seas done. Lord of Atlantis, Master of the Pearl Trident, Silvergill Adept, Cursecatcher, Merfolk Trickster absent. ~20%.
- **Death and Taxes** — Low-mid. Aether Vial done (mana-free creature drop — the deck's signature). Stoneforge Mystic done. Solitude done. Thalia, Guardian of Thraben + Skyclave Apparition + Leonin Arbiter absent. ~30%.

## Top 20 Modern staples NOT yet implemented

Sorted roughly by build priority (small infra lift × high meta share). Refreshed against origin/main `8dc0625` (post-Pyromancer's Goggles). Round 2 of the refresh — round 1's list was almost entirely shipped (Living End, Karn Liberated, Tron lands, Ragavan, Sigarda's Aid, Puresteel Paladin, Manabarbs, Plague Engineer, Pyromancer's Goggles).

| # | Card | Difficulty | Blocker |
|---|---|---|---|
| ~~1~~ | ~~Ponder~~ | ~~low~~ | Shipped via `PonderFactory` — peek top 3 + agent-driven reorder (ScryDecision with empty ToBottom) + draw. "May shuffle" rider deferred (no `IZone.Shuffle` entry point yet). |
| 2 | Brainstorm | low | Instant {U} — draw 3, put 2 back in any order. Existing `LookAtTopPutKInHand` doesn't cover the post-draw-replace shape; needs draw-then-return-N-to-top template. |
| ~~3~~ | ~~Preordain~~ | ~~low~~ | Shipped via `PreordainFactory` — scry 2 + draw 1 (delegates to standard `ScryAction` pipeline). The `LibrarySpellFactory.ScryNSpell` tail-detection path covers the same shape via the data-driven binder. |
| ~~4~~ | ~~Mystical Tutor~~ | ~~low~~ | Shipped via `MysticalTutorFactory` — Instant {U}, predicate filters library to `CardType.Instant`/`Sorcery`, agent-driven pick (deterministic first-match fallback) inserted at library index 0 via `IZone.InsertCardAt`. Shuffle deferred (no `IZone.Shuffle` entry point yet — same rationale as the rest of `SearchSpellFactory`). |
| ~~5~~ | ~~Swords to Plowshares~~ | ~~low~~ | Shipped via `SwordsToPlowsharesFactory` — exile target creature + lifegain by power read via `Creature.Power` (live `Compute` feeds through). Snapshot before zone move (CR 112.7a); negative power floors to zero (CR 119.3); illegal-target → no-op (CR 608.2b). |
| ~~6~~ | ~~Spell Queller~~ | ~~medium~~ | Shipped via `SpellQuellerFactory` — Creature — Spirit {1}{W}{U} 2/3 Flash. ETB: target-spell request (Snapcaster-style `SetChosenTargets`), validates mv ≤ 4 at resolution (CR 202.3b incl. PendingCastX), removes the spell from the stack via `OracleSpellBinder.RemoveFromStack`, places the underlying card into its owner's exile zone (closure-captured for LTB). LTB: `CardMovedEvent.FromZone == Battlefield` trigger fires `onExiledCardReleased` callback; host drives the free-cast via `CastFromExileAlternativeCost` against the original owner (factory exposes `BuildFreeCastCost`). |
| ~~8~~ | ~~Sun Titan~~ | ~~medium~~ | Shipped via `SunTitanFactory` — {4}{W}{W} 6/6 Giant with Vigilance + ETB/attack triggers that reanimate target permanent card (any of artifact/creature/enchantment/land/planeswalker — CR 110.4) with mana value ≤ 3 from controller's graveyard. Shared `ReanimatePermanentPick` mirrors `PriestOfFellRitesFactory.ReanimatePick` but widens from `Creature` to `Permanent`; routes graveyard→battlefield through ZoneService when supplied so ETB triggers fire on the reanimated permanent (CR 603.6a). Deterministic first-match v1; "you may" + target prompt deferred (same as Priest of Fell Rites / Primeval Titan). |
| ~~9~~ | ~~Skullclamp~~ | ~~medium~~ | Shipped via `SkullclampFactory` — Equipment {1} with `AttachedBoostEffect(+1, -1)` at Layer 7c; dies trigger (CR 603.6c) keyed via `CardMovedEvent` to the source's current `AttachedTo` and draws 2 cards; Equip {1}. Sorcery-speed gate + attach-target prompt deferred (same as Colossus Hammer). |
| ~~10~~ | ~~Umezawa's Jitte~~ | ~~medium~~ | Shipped via `UmezawasJitteFactory` — Legendary Equipment {2} + combat-damage trigger by equipped creature adds 2 charge counters via new `CounterType.Charge` on the equipment + three modal activated abilities, each paid by new `RemoveChargeCounterCost`: (1) 2 damage to any target, (2) -1/-1 EOT via `PumpUntilEndOfTurnEffect`, (3) gain 2 life; Equip {2}. Modes fanned out into three separate activated abilities — native modal-activated infra deferred. |
| ~~11~~ | ~~Sword of Fire and Ice~~ | ~~medium~~ | Shipped via `SwordOfFireAndIceFactory` — Equipment {3} with `AttachedBoostEffect(+2, +2)` at Layer 7c; protection-from-red + protection-from-blue ride as two `ProtectionAbility` markers on the equipment card (full DEBT-A enforcement deferred — no attachment-aware Layer 6 grant for protection yet); combat-damage-to-a-player trigger (CR 510) deals 2 damage to any target via `OracleSpellBinder.DealDamage` + draws 1 (damage half no-ops without a chosen target while the paired draw still resolves per CR 608.2b); Equip {2}. Sorcery-speed gate + attach-target prompt deferred (same as Colossus Hammer / Skullclamp / Jitte). Sword of Feast and Famine (`SwordOfFeastAndFamineFactory`) follow-up swaps the markers-on-the-card approach for true attached-grant lifecycles via `AttachedAuraAbilityGrantStaticEffect` — the equipped creature picks up live `ProtectionAbility("black")` + `ProtectionAbility("green")` and `Rules.Protection.HasProtectionFromColor` resolves through the bearer, closing the DEBT-A grant gap for this Sword. |
| ~~12~~ | ~~Wasteland~~ | ~~low~~ | Shipped via `WastelandFactory` — {T}: Add {C} (vanilla ManaAbility) + {T}, Sacrifice Wasteland: destroy target nonbasic land (ActivatedAbility with TargetRequest, self-sacrifice inline at resolution while AdditionalCost.Sacrifice is stubbed — mirrors Karakas's destroy/bounce shape). |
| ~~13~~ | ~~Mutavault~~ | ~~medium~~ | Shipped via `MutavaultFactory` — Land with {T}: Add {C} mana ability + {1} activated ability that until EOT registers Layer 4 `MutavaultAnimateEffect` (add Creature type + every modelled creature subtype, CR 613.1c) and Layer 7b `MutavaultBecomesPTEffect` (set-base P/T 2/2 — shim pattern mirroring `KarnAnimatedShimPTEffect` since Mutavault is a Land runtime instance and `Compute(Permanent)` seeds a chars row with no P/T fields). Both effects flagged `ExpiresAtEndOfTurn` (CR 514.2). "Every creature type" approximated as every creature subtype currently enumerated in `CardSubtype`; auto-grows with the enum. |
| ~~14~~ | ~~Inkmoth Nexus~~ | ~~medium~~ | Shipped via `InkmothNexusFactory` — Land {T}: Add {C} + {1}: until EOT, becomes a 1/1 Phyrexian Insect artifact creature with flying + infect (still a land). `InkmothAnimateLandEffect` is a Layer-4 type/subtype/keyword grant + 1/1 P/T shim (parallels `KarnAnimatedShimPTEffect` / Mutavault's pair — non-Creature C# permanents don't surface layer-7b through `Compute(Permanent)` yet). Infect mechanic itself (poison counters + creature damage as -1/-1 counters, CR 702.90) is deferred — keyword marker only. |
| ~~15~~ | ~~Goblin Matron~~ | ~~low~~ | Shipped via `GoblinMatronFactory` — Creature {2}{R} 1/1 Goblin + ETB tutor filtered by `CardSubtype.Goblin` (agent-driven pick with deterministic first-match fallback, mirroring `MysticalTutorFactory` / `SearchSpellFactory`). Shuffle + reveal event deferred (same gaps as the rest of the tutor surface). |
| ~~16~~ | ~~Trinket Mage~~ | ~~low~~ | Shipped via `TrinketMageFactory` — Creature — Human Wizard {2}{U} 2/2 with ETB triggered ability that tutors the first artifact card with `ManaCostValue.TotalValue <= 1` from library → hand (deterministic first-match; shuffle + reveal-event deferred, same as Stoneforge Mystic). Registered in the `NamedCardFactory` dispatch and exercised by 6 unit tests covering identity, dispatch, mv-0 / mv-1 tutor happy paths, mv≥2-only no-op, and no-artifacts no-op. |
| ~~17~~ | ~~Sensei's Divining Top~~ | ~~high~~ | Shipped via `SenseisDiviningTopFactory` — Artifact {1} with two activated abilities. {T}: peek top 3 + agent-driven reorder via `ScryAction.Peek` / `ScryAction.Apply` with `ToBottom=[]` (mirrors Ponder; default preserves order). {1}, {T}: draw a card (empty library flags `MarkTriedToDrawFromEmptyLibrary` per CR 704.5b) then move Top from battlefield to library index 0 via `IZone.InsertCardAt`. Printed Legendary supertype omitted in v1. |
| 18 | Daze | low | Instant {1}{U} — counter unless pay {1}; alt cost {0} + return an Island you control. Counter-unless-pay exists; needs alt-cost with non-mana additional cost (bounce-self-permanent), composable with `PitchAlternativeCost`-style probe. |
| ~~19~~ | ~~Boros Reckoner~~ | ~~high~~ | Shipped via `BorosReckonerFactory` — 3/3 Minotaur Wizard {R/W}{R/W}{R/W} + First strike + damage-received trigger (CR 603.1) redirecting the captured damage amount to a settable any-target via `OracleSpellBinder.DealDamage`. v1 simplification: ships as a triggered ability rather than the printed source-damage *replacement* effect (CR 614); damage still resolves on Boros Reckoner before the redirect fires, and the redirect goes on the stack. Source-damage redirect primitive — a generalisation of `PreventNextDamageFromChosenSourceShield` — still on the backlog for the true replacement-effect upgrade. |
| ~~20~~ | ~~Reckless Charge~~ | ~~low~~ | Shipped via `RecklessChargeFactory` — Sorcery {R}. Resolve-time `SpellDefinition` declares one 1..1 "target creature" request; on resolution registers `PumpUntilEndOfTurnEffect(+3/+0)` and `GrantKeywordUntilEndOfTurnEffect("Haste")` on the target's `ActiveEffects` (CR 514.2 — both expire at cleanup). Flashback alt-cost `{2}{R}` parsed from oracle via `FlashbackOracleParser` (mirrors Faithless Looting); post-resolve exile via `FlashbackAlternativeCost.OnResolved` (CR 702.34b). Illegal-target / missing-ActiveEffects fallbacks no-op cleanly. |

## How to update this doc

After merging a card-shipping PR:
1. Append the new card to the **Named factories** table (or **Template-covered (notable)** if it's a template port).
2. Remove it from **Top 20 not yet implemented** if it appears there.
3. Bump the **Last updated** date and **Latest origin/main** hash at the top.

After merging a mechanic-infra PR:
1. Flip the relevant row in **Costs**, **Effects**, **Keywords**, or **Targeting / cast flow** to **Done (#PR)**.
2. If a top-20 entry's blocker is resolved, drop its difficulty one tier in the priority table.

Keep this file terse. New cards = one row. New mechanics = one row. Long-form rationale lives in PR descriptions, not here.
