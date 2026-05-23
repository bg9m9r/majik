namespace Majik.Console;

/// <summary>Hardcoded bootstrap list of card names the engine implements
/// as of the most recent release. Maintainer adds to this list as the
/// engine gets new cards; the <c>seed-implemented</c> subcommand applies
/// it to the local SQLite card DB.</summary>
public static class SeedImplementedCards
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        // Basic lands
        "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes",
        // Vanilla creatures
        "Grizzly Bears", "Hill Giant", "Bear Cub", "Savannah Lions",
        "Goblin Piker", "Walking Corpse", "Phantom Warrior",
        "Llanowar Elves", "Runeclaw Bear", "Centaur Courser",
        // Shock lands (CR 614 replacement effect — ShockLandBinder)
        "Overgrown Tomb",
        // Fetch lands — Onslaught + Zendikar cycles (OracleLandActivatedAbilityBinder)
        "Bloodstained Mire", "Flooded Strand", "Polluted Delta", "Wooded Foothills", "Windswept Heath",
        "Misty Rainforest", "Scalding Tarn", "Verdant Catacombs", "Marsh Flats", "Arid Mesa",
        // Undying creatures (CR 702.93 — UndyingFactory)
        "Young Wolf", "Strangleroot Geist", "Butcher Ghoul", "Geralf's Messenger",
        // Removal + discard (OracleSpellBinder)
        "Fatal Push", "Thoughtseize", "Inquisition of Kozilek",
        // Dies-trigger land destruction (OracleTriggeredAbilityBinder — dies-destroy-land)
        "Fulminator Mage",
        // ETB graveyard-to-library trigger (OracleTriggeredAbilityBinder); Flash+Reach via KeywordBinder
        "Endurance",
        // X-cost green creature tutor; puts target directly onto battlefield (OracleSpellBinder)
        // Note: post-resolution self-return-to-library deferred (v1 goes to graveyard instead).
        "Green Sun's Zenith",
        // Pitch spell — exile a green card to destroy up to 2 artifacts/enchantments
        // (OracleSpellBinder: DestroyUpToArtifactEnchantmentSpell + ExileColoredCardAlternativeCost).
        // v1: "if it's not your turn" timing restriction not enforced.
        "Force of Vigor",
        // Artifact Creature — Construct 0/0 (WalkingBallistaFactory).
        // {4} grow and counter-removal ping wired; ETB X counters + sorcery-speed + ping targeting deferred.
        "Walking Ballista",
        // Legendary Creature — Phyrexian Human Cleric 2/4 (YawgmothFactory).
        // Pay 1 life + sacrifice another creature; effects 1+2+4 wired (lose life, discard, draw).
        // Protection from Humans, sacrifice target prompt, -1/-1 counter (effect 3) deferred.
        "Yawgmoth, Thran Physician",
        // Legendary Planeswalker — Grist {1}{B}{G} loyalty 3 (OracleLoyaltyAbilityBinder).
        // +1: Create 1/1 Insect token — wired.
        // −2: Return target creature from graveyard to battlefield — wired (v1 auto-picks first, no targeting prompt).
        // −5: Emblem (whenever a creature dies, opponent reveals top card; if creature, you draw) — deferred (no emblem system).
        // Static: "as long as not on battlefield, it's a 1/1 Insect" — deferred (Layer 4 continuous type effect).
        // "+1: Each opponent loses 1 life and you gain 1 life" multi-effect — deferred (multi-effect parsing).
        "Grist, the Hunger Tide",
        // Legendary Planeswalker — Ashiok {1}{U}{B} loyalty 3 (OracleLoyaltyAbilityBinder).
        // −1: Each opponent mills four cards — wired (requires allPlayers to be passed to Bind).
        // −7: Exile up to four target cards from graveyards — deferred (targeting + exile-from-graveyard).
        // Static: "Players who don't control Ashiok can't search libraries" — deferred (replacement effect).
        "Ashiok, Dream Render",
        // Legendary Land — Kamigawa: Neon Dynasty (BoseijuFactory).
        // {T}: Add {G} — wired.
        // Channel — {1}{G}, Discard ~: Destroy target artifact/enchantment/nonbasic land — costs wired,
        // destroy effect deferred (no targeting). ETB-tapped restriction + basic-land-search follow-up deferred.
        "Boseiju, Who Endures",
        // Artifact — {2} (TorporOrbFactory). Static effect suppresses creature ETB triggers
        // (CR 614 / CR 603.3). TorporOrbStaticEffect increments TriggerManager.CreatureEtbTriggerSuppressionCount
        // while the Orb is on the battlefield; TriggerManager.EvaluateTriggers gates creature-ETB events.
        // Multiple simultaneous Orbs stack correctly. Common sideboard hate vs. CiP creatures.
        "Torpor Orb",
        // U/B dual surveil land — Murders at Karlov Manor (UndergroundMortuaryFactory).
        // {T}: Add {U} and {T}: Add {B} — two ManaAbility instances, player selects at activation.
        // ETB trigger: surveil 1 — default-all-graveyard decision wired.
        // ETB-tapped restriction + untapped gate on surveil trigger + player prompt deferred.
        // Same pattern reusable for other surveil duals: Commercial District, Lush Portico,
        // Meticulous Archive, Raucous Theater, Shadowy Backstreet.
        "Underground Mortuary",
        // Land Creature — Forest Dryad 1/1 (DryadArborFactory).
        // No mana cost (CR 305.8). Creature + AddCardType(Land); Forest + Dryad subtypes.
        // {T}: Add {G} wired directly (not via Basic-land guard in OracleManaBinder).
        // Useful for Green Sun's Zenith (fetch as a Forest creature); targeting deferred.
        "Dryad Arbor",
        // Legendary Creature — Halfling Citizen 1/2 (DelightedHalflingFactory).
        // {T}: Add one mana of any color — 5 ManaAbility instances (one per WUBRG) wired.
        // Usage restriction (legendary-only mana) + "spell can't be countered" rider deferred.
        "Delighted Halfling",
        // Land — Bloomburrow (WastewoodVergeFactory).
        // {T}: Add {G} and {T}: Add {B} — two ManaAbility instances wired.
        // {B} activation restriction ("if you control a Swamp or Forest") deferred.
        "Wastewood Verge",
        // Land — Bloomburrow (SpymastersVaultFactory).
        // {T}: Add {B} — wired.
        // ETB-tapped restriction + connive activated ability deferred.
        "Spymaster's Vault",
        // Artifact — {1} (VexingBaubleFactory).
        // {1}, {T}, Sacrifice: Draw a card — wired.
        // "Counter free spells" triggered ability deferred.
        "Vexing Bauble",

        // Sorcery — {1}{G} (OracleSpellBinder: MalevolentRumblePattern).
        // Reveal top 4, auto-pick first permanent → hand, rest → graveyard,
        // create 1 Eldrazi Spawn 0/1 token (ManaAbility {C}).
        // "You may" opt-out + real player choice deferred.
        "Malevolent Rumble",

        // Enchantment — {1}{G} (DredgersInsightFactory).
        // ETB: mill 4, auto-pick first artifact/creature/land → hand.
        // Lifegain trigger: artifact/creature leaves controller's graveyard → gain 1 life.
        // "You may" opt-out + batched simultaneous-leavers deferred.
        "Dredger's Insight",

        // Creature — Insect Warrior {1}{G} 3/2 (KraulHarpoonerFactory).
        // Reach + ETB Undergrowth +X/+0 EOT wired; targeting, fight, "you may" deferred.
        "Kraul Harpooner",

        // Creature — Bear {G} 1/1 (BadgermoleCubFactory). Shell only.
        // Earthbend 1 ETB + tap-creature-for-mana trigger deferred.
        "Badgermole Cub",

        // Creature — Orc Archer {1}{B} 1/1 (OrcishBowmastersFactory).
        // Flash keyword wired.
        // ETB damage-any-target trigger, opponent-draw watcher, amass Orcs 1 deferred.
        "Orcish Bowmasters",

        // Artifact — {2} (AgathasSoulCauldronFactory).
        // {T}: exile first card from controller's graveyard; if creature card, +1/+1 counter
        // on first creature on controller's battlefield — wired (v1 auto-pick).
        // Static mana-color-substitute + ability-grant via imprint deferred.
        "Agatha's Soul Cauldron",

        // ---- Bot-deck spell coverage (template-bound) ----
        // Instant — {R}. DamageAnyTargetTemplate.
        "Lightning Bolt",
        // Sorcery — {R}. DamagePlayerTemplate ("target player or planeswalker").
        "Lava Spike",
        // Instant — {R}. DamageAnyTargetTemplate.
        // Flashback—Sacrifice a Mountain (CR 702.34) wired via
        // FlashbackAlternativeCost + SacrificeBasicLandCost + FlashbackOracleParser.
        // Production bot doesn't yet auto-elect flashback (PriorityAction
        // can't carry alt-cost) — same status as Force of Vigor's pitch.
        "Lava Dart",
        // Sorcery — {2}{R} (spectacle {R}). DamageAnyTargetTemplate handles
        // the 3-damage body. Spectacle alt-cost ("if an opponent lost life
        // this turn") wired via SpectacleBinder + SpectacleAlternativeCost
        // (CR 702.118) — dispatcher offers the {R} alt-cost when any
        // opponent's Player.LifeLostThisTurn > 0 at announce time.
        "Skewer the Critics",
        // Instant — {G/P}. PumpCreatureTemplate applies +2/+2 EOT. Phyrexian
        // alt-cost (2 life instead of {G}) deferred.
        "Mutagenic Growth",
        // Instant — {W}. CounterUnlessPayTemplate ({1} to keep).
        "Mana Tithe",
        // Instant — {U}. CounterUnlessPayTemplate now recognizes the
        // "noncreature" type qualifier and routes to the typed factory,
        // so Spell Pierce ("Counter target noncreature spell unless its
        // controller pays {2}.") binds + resolves without a new template.
        "Spell Pierce",
        // Instant — {U}{U}. CounterTargetSpellTemplate ("Counter target spell.").
        // The canonical hard counter; binds via the existing template registry
        // with no new factory.
        "Counterspell",

        // ---- Bot-deck land coverage (ShockLandBinder + OracleManaBinder) ----
        // Land — Mountain Plains. Shock-land replacement (pay 2 life or ETB
        // tapped) via ShockLandBinder. {T}: Add {R} or {W} via OracleManaBinder
        // dual-modal pattern.
        "Sacred Foundry",
        // Land — Island Mountain. Same shock pattern: {T}: Add {U} or {R}.
        "Steam Vents",

        // Creature — Human Monk {R} 1/2 (KeywordBinder).
        // Haste + Prowess — both wire via KeywordBinder once the game's
        // ContinuousEffectsService is plumbed through (CR 613).
        // Prowess: whenever you cast a noncreature spell, +1/+1 EOT.
        "Monastery Swiftspear",

        // Sorcery — {R} (DealsNDamageEachOpponentTemplate).
        // "~ deals 3 damage to each opponent" loops ChosenSpellParams.AllPlayers
        // minus the caster and applies LoseLife. Works in production now that
        // SpellCastFlow plumbs AllPlayers through.
        "Boltwave",

        // Instant — {R}{W} (ModalChooseOneTemplate). Mode 1 (4 damage to
        // player/walker) binds via DamagePlayer. Modes 2/3 (mass
        // indestructible, target double strike) no-op until templates exist.
        "Boros Charm",

        // Instant — {W}{W} (ModalChooseOneTemplate). Mode 2 (destroy target
        // enchantment) binds via DestroyArtifactEnchantment. Modes 1/3
        // (variable damage by creature count, exile graveyards) no-op.
        "Thraben Charm",

        // Creature — Goblin Scout {R} 2/2 (KeywordBinder + OracleTriggeredAbilityBinder).
        // Haste — wired via KeywordBinder.
        // Attack trigger — "defending player reveals top of library; if it's a
        // land, that player puts it into their hand" — wired via
        // OracleTriggeredAbilityBinder.GoblinGuideFullPattern; defender is
        // captured from CreatureAttacksEvent at trigger time.
        // Deferred: explicit CardRevealedEvent for the reveal half (current
        // v1 only emits the move-to-hand on the land branch).
        "Goblin Guide",

        // Legendary Creature — Monkey Pirate {R} 2/1 (OracleTriggeredAbilityBinder).
        // Combat-damage trigger: "create a Treasure token and exile the top
        // card of that player's library" — Treasure handled by
        // CreateTreasure regex; exile handled by ExileTopOfThatPlayersLibrary
        // inline effect that captures the damaged player from
        // CombatDamageDealtEvent.
        // Deferred:
        //  - "Until end of turn, you may cast that card" — no
        //    temporary-cast-permission/alt-zone-casting system in v1.
        //  - Dash {1}{R} — alt-cost framework + EOT bounce trigger
        //    (returns from battlefield to hand at next end step) not wired.
        //  - Treasure token's "sacrifice for one mana of any color" already
        //    wired via TokenFactory.CreateTreasure (5 ManaAbility options).
        "Ragavan, Nimble Pilferer",

        // Artifact — {0} (MishrasBaubleFactory).
        // {T}, Sacrifice this artifact: Look at top of target player's library;
        // draw a card at the beginning of the next turn's upkeep.
        // Look-at-top + sacrifice wired; delayed upkeep draw wired via
        // DelayedTriggeredAbility registered with TriggerManager (CR 603.7).
        // v1 auto-targets the controller (real player-targeting prompt deferred).
        "Mishra's Bauble",

        // Enchantment — {1}{R} (GoblinBombardmentFactory).
        // Sacrifice a creature: This enchantment deals 1 damage to any target.
        // Activation cost (SacrificeCreatureCost) + 1 damage wired via
        // OracleSpellBinder.DealDamage. v1 picks target via BuildPingAbility's
        // explicit parameters (bot pick-first-legal heuristic). Real prompt
        // system deferred.
        "Goblin Bombardment",

        // Sorcery — {U}{R} (OracleSpellBinder: ExpressiveIterationTemplate).
        // Look at top 3 of your library — first → hand, second → bottom of
        // library, third → exile (deterministic v1 distribution; real
        // player choice deferred).
        // "You may play the exiled card this turn" rider deferred — no
        // temporary cast-from-exile permission system yet.
        "Expressive Iteration",

        // Enchantment Creature — Spirit {R}{R} 2/2 (OracleTriggeredAbilityBinder).
        // "Whenever a player casts a spell with mana value 3 or less, ~ deals
        // 2 damage to that player." Wired via PlayerCastsCheapSpellLine
        // regex + DealDamageOpponent. v1 simplification: damages every
        // non-controller player (correct for 2-player; multiplayer
        // "that player" accuracy deferred).
        "Eidolon of the Great Revel",

        // ---- Sagas (SagaBinder per-card chapter effects) ----

        // Enchantment — Saga {2}{R} // Enchantment Creature — Goblin Shaman
        // (Kamigawa: Neon Dynasty). DFC stored under its full composite name.
        // I  — Create a 2/2 red Goblin Shaman token (embedded
        //      "Whenever this token attacks, create a Treasure token"
        //      trigger DEFERRED — no attack-trigger wiring for token
        //      abilities yet).
        // II — Discard up to two, draw that many — wired; "you may" opt-out
        //      and per-card choice DEFERRED (v1 discards the first two cards
        //      in hand deterministically).
        // III— Exile + return transformed to Reflection of Kiki-Jiki —
        //      DEFERRED (no saga-transform infrastructure).
        "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki",

        // Enchantment — Saga {2}{R}{R} // Legendary Creature — Avatar
        // (Avatar: The Last Airbender). DFC stored under its full composite name.
        // I  — Exile top 3 of library — wired; "you may play those cards
        //      until end of your next turn" rider DEFERRED (needs alt-play /
        //      turn-scoped permission framework).
        // II — Add one mana of any color — wired as {R} deterministically;
        //      real mana-color prompt DEFERRED.
        // III— Exile + return transformed to Avatar Roku — DEFERRED (no
        //      saga-transform infrastructure).
        "The Legend of Roku // Avatar Roku",

        // Creature — Elemental Incarnation {3}{W}{W} 3/2 (SolitudeFactory).
        // Modern Horizons 2 incarnation. Flash + Lifelink + Evoke keyword
        // markers via KeywordBinder; KeywordBinder also attaches the printed
        // evoke sacrifice trigger (EvokeFactory) automatically. ETB
        // exile-target-creature + lifegain wired via SolitudeFactory.
        // Evoke alt-cost — "exile a white card from your hand" — via
        // EvokeAlternativeCost.
        // Deferred: opponent pitch-back ("controller may exile a
        // non-Elemental, non-Incarnation white card to return the exiled
        // creature").
        "Solitude",

        // Creature — Elemental Incarnation {3}{U} 3/3 (SubtletyFactory).
        // Modern Horizons 2 incarnation — blue counterpart to Solitude.
        // Flash + Evoke keyword markers via KeywordBinder; KeywordBinder
        // attaches the printed evoke sacrifice trigger (EvokeFactory)
        // automatically. ETB bounce-and-look trigger wired via SubtletyFactory:
        // return target opponent's creature/planeswalker to its owner's hand,
        // then that owner does a 1-card "look + may bottom" scry decision
        // sourced from their registered IPlayerAgent. Evoke alt-cost —
        // "exile a blue card from your hand" — via EvokeAlternativeCost.
        "Subtlety",

        // U/R Horizon Canopy painless dual — Modern Horizons (FieryIsletFactory).
        // {T}, Pay 1 life: Add {U} or {R} — two ManaAbility instances with a
        // life-cost activation gate (CR 119.4) + LoseLife side-effect wired
        // via HorizonLandBinder.
        // {1}, {T}, Sacrifice this land: Draw a card — wired (Vexing Bauble shape).
        // Sacrifice cost doesn't yet move the land to the graveyard (zone-
        // service plumbing TODO on AdditionalCost.Sacrifice).
        // "Pay life as a 'you may' prompt" is moot — bot's source-picker uses
        // the ability transparently when paying mana costs.
        "Fiery Islet",

        // R/W Horizon Canopy painless dual — Modern Horizons (SunbakedCanyonFactory).
        // Same shape as Fiery Islet — Pay 1 life mana abilities + sac-draw.
        // Same deferred notes apply (sacrifice zone movement).
        "Sunbaked Canyon",

        // U/R surveil land — Foundations (ThunderingFallsFactory).
        // {T}: Add {U} or {R} — two ManaAbility instances, player selects at activation.
        // ETB trigger: surveil 1 — default-all-graveyard decision wired.
        // ETB-tapped handled by EntersTappedBinder in production path.
        // Surveil player prompt + life-loss replacement effects out of scope here.
        // Same pattern reusable for the rest of the Foundations surveil cycle.
        "Thundering Falls",

        // R/W surveil land — Foundations (ElegantParlorFactory).
        // Same shape as Thundering Falls; only colour differs. Same deferred notes.
        "Elegant Parlor",

        // R/W fastland — Kaladesh (InspiringVantageFactory).
        // {T}: Add {R} or {W} — two ManaAbility instances wired.
        // Conditional ETB-tapped ("unless you control two or fewer other lands")
        // handled by ConditionalEntersTappedBinder in production path
        // (regex already matches the "N or fewer / more other lands" form
        // shared with Kamigawa channel lands).
        // Same pattern reusable for the rest of the Kaladesh fastland cycle:
        // Concealed Courtyard, Spirebluff Canal, Botanical Sanctum, Blooming Marsh.
        "Inspiring Vantage",

        // ---- Modern staples shipped in the 2026-05-23 bump ----

        // Enchantment — {2}{R} (CR 305.6). Nonbasic lands lose all land types,
        // abilities, and are Mountains. Wired via BloodMoonStaticEffect +
        // generalized SubtypeReplaceStaticEffect (also reused by Magus of the
        // Moon, Harbinger of the Seas, Conversion).
        "Blood Moon",
        // Creature — Human Wizard {2}{R} 2/2 — same static effect as Blood Moon.
        "Magus of the Moon",
        // Creature — Merfolk Wizard {2}{U} 2/2 — Islands variant of Blood Moon.
        "Harbinger of the Seas",
        // Enchantment — {2}{W}{W} — Mountains become Plains static effect.
        "Conversion",
        // Land — Legendary. Each Swamp on the battlefield is also a Swamp in
        // addition to its other types; additive subtype grant (CR 305.7).
        "Urborg, Tomb of Yawgmoth",
        // Land — Legendary. Each land is also a Forest in addition to its other types.
        "Yavimaya, Cradle of Growth",
        // Enchantment — Aura {1}{U}. Enchanted land is an Island (CR 303.4 +
        // 305.7) and loses other land types. Wired via SpreadingSeasFactory.
        "Spreading Seas",
        // Enchantment — Aura {U}. Same retyping pattern as Spreading Seas
        // without the cantrip.
        "Sea's Claim",
        // Creature — Human Wizard {U}{R} 2/1 (SnapcasterMageFactory).
        // Flash + ETB grants flashback to target instant/sorcery in graveyard
        // (CR 702.33). Cost = card's mana cost (no replacement of {X}).
        "Snapcaster Mage",
        // Legendary Planeswalker — Teferi {1}{W}{U} loyalty 4 (TeferiTimeRavelerFactory).
        // +1: Sorcery-speed lockout for opponents until your next turn.
        // −3: Return target nonland permanent to its owner's hand + draw 1.
        // Static: cast sorceries any time you could cast an instant — wired via
        // emblem-with-static infrastructure.
        "Teferi, Time Raveler",
        // Creature — Human Wizard {1}{B} 2/1 (DarkConfidantFactory).
        // Upkeep trigger: reveal top, put into hand, lose life equal to MV.
        "Dark Confidant",
        // Legendary Planeswalker — Liliana {1}{B}{B} loyalty 3 (LilianaOfTheVeilFactory).
        // +1 each-discard, −2 raise dead, −6 partition permanents.
        "Liliana of the Veil",
        // Legendary Planeswalker — Wrenn {R}{G} loyalty 3 (WrennAndSixFactory).
        // +1 land-from-grave, −1 1-damage-to-any-target, −7 emblem retrace.
        "Wrenn and Six",
        // Creature — Human Cleric {1}{G} 2/1 (ScavengingOozeFactory).
        // {G}: Exile target card from a graveyard; if creature, put a +1/+1
        // counter on ~ and you gain 1 life.
        "Scavenging Ooze",
        // Artifact — {1} (PithingNeedleFactory).
        // As ~ enters, name a card. Activated abilities of sources with the
        // chosen name can't be activated unless they're mana abilities
        // (CR 605 / 614 suppression).
        "Pithing Needle",
        // Instant — {R} (UnholyHeatFactory).
        // ~ deals 2 damage to any target. Delirium — deals 6 instead if four
        // or more card types among cards in your graveyard.
        "Unholy Heat",
        // Instant — {1}{U}{U}{U} (CR 700.2d modal choose-N template).
        // Choose two: counter target spell; return target permanent to hand;
        // tap all creatures your opponents control; draw a card.
        "Cryptic Command",
        // Instant — {Phyrexian/B} (SurgicalExtractionFactory).
        // Phyrexian-cost global name-exile of graveyard + library + hand copies.
        "Surgical Extraction",
        // Creature — Bird Advisor {1}{U} 1/3 (LedgerShredderFactory).
        // Flying + "whenever you cast your second spell each turn, surveil 2".
        "Ledger Shredder",
        // Creature — Dragon {U}{U}{R} */* (MurktideRegentFactory).
        // Delve + ETB X-counters (CR 122.1g) based on instants/sorceries exiled.
        "Murktide Regent",
        // Enchantment Creature — Human {1}{U} 1/1 (DressDownFactory).
        // Flash + creatures lose all abilities and base power/toughness become
        // 1/1 (CR 613.6 + 613.7b).
        "Dress Down",
        // Creature — Cleric {W}{B}{B} 3/2 (PriestOfFellRitesFactory).
        // ETB reanimate target creature card with MV ≤ 3 from graveyard;
        // graveyard-activated unearth for a one-shot battlefield return.
        "Priest of Fell Rites",
        // Creature — Avatar {3}{B}{B}{B} */* (DeathsShadowFactory).
        // CDA — base P/T equals (13 − controller's life total) / same (CR 613.2,
        // Layer 7a). Wired via DeathsShadowCharacteristicDefinition.
        "Death's Shadow",
        // Instant — {U} (ConsiderFactory).
        // Look at top card of your library, then mill it if you want, then draw.
        "Consider",
        // Instant — {U} (StubbornDenialFactory).
        // Counter target noncreature spell. Ferocious — if you control a 4-power
        // or greater creature, counter that spell unless its controller pays {3}.
        "Stubborn Denial",
        // Sorcery — {G} (AncientStirringsFactory).
        // Look at top 5 of library; reveal a colorless card → hand, rest → bottom.
        "Ancient Stirrings",
        // Creature — Elemental Incarnation {3}{B}{B} 3/2 (GriefFactory).
        // Flash + Menace + ETB target opponent discards a card. Evoke alt-cost:
        // exile a black card from your hand. Companion to Solitude.
        "Grief",
        // Enchantment — {2}{G} (UpTheBeanstalkFactory).
        // ETB draw a card; whenever you cast a spell with mana value 5+, draw a card.
        "Up the Beanstalk",
        // Legendary Planeswalker — Karn {7} loyalty 6 (KarnLiberatedFactory).
        // +4: target player exiles a card from hand; -3: exile target permanent;
        // -14: restart game with exiled non-Aura permanents (deferred).
        "Karn Liberated",
        // Land — Urza Tron piece (UrzaTronLandFactory: Mine).
        // {T}: Add {C}. If you control Urza's Mine, Urza's Power-Plant, and
        // Urza's Tower, add {2} instead.
        "Urza's Mine",
        // Land — Urza Tron piece (UrzaTronLandFactory: Tower).
        // {T}: Add {C}. If you control all three Urza lands, add {3} instead.
        "Urza's Tower",
        // Land — Urza Tron piece (UrzaTronLandFactory: Power-Plant).
        // {T}: Add {C}. If you control all three Urza lands, add {2} instead.
        "Urza's Power Plant",
        // Artifact — {1} (AmuletOfVigorFactory).
        // Whenever a permanent enters tapped under your control, untap it.
        "Amulet of Vigor",
        // Creature — Giant {4}{G}{G} 6/6 (PrimevalTitanFactory).
        // ETB + attack trigger: search library for up to two land cards, put
        // them onto the battlefield tapped.
        "Primeval Titan",

        // ---- Modern staples shipped in the 2026-05-23c bump ----

        // Legendary Planeswalker — Karn {4} loyalty 5 (KarnTheGreatCreatorFactory).
        // Static: activated abilities of artifacts your opponents control
        // can't be activated (mana-ability exempt, CR 605). +1: target
        // noncreature artifact becomes a creature with P/T equal to its
        // mana value. -2: wishboard — fetch an artifact from outside-the-game.
        "Karn, the Great Creator",
        // Sorcery — {1}{R} (TribalFlamesFactory). Domain damage —
        // ~ deals damage equal to the number of basic land types you
        // control to any target (CR 702.16).
        "Tribal Flames",
        // Sorcery — {S}{S}{S} suspend-only (CrashingFootfallsFactory).
        // Has no mana cost; cascade-enabled via Suspend 4 — {1}{R}.
        // Create two 4/4 green Rhino creature tokens with trample.
        "Crashing Footfalls",
        // Legendary Creature — Cat Nightmare {W}{B} 3/2 (LurrusOfTheDreamDenFactory).
        // Each turn, you may cast one permanent spell with mana value 2 or
        // less from your graveyard. Companion clause deferred (no companion
        // system).
        "Lurrus of the Dream-Den",
        // Artifact — Equipment {1} (ColossusHammerFactory).
        // Equipped creature gets +10/+0 and loses flying. Equip {8}.
        "Colossus Hammer",
        // Sorcery — {3}{B}{B}{B} suspend-only (LivingEndFactory).
        // Cascade-enabled via Suspend 3 — {2}{B}. Each player exiles all
        // creatures from their graveyards, sacrifices all creatures they
        // control, then returns the exiled cards to the battlefield.
        "Living End",
        // Enchantment — {1}{W} (SigardasAidFactory). Eldritch Moon.
        // You may cast Aura and Equipment spells as though they had flash.
        // Whenever an Equipment enters under your control, you may attach
        // it to target creature you control.
        "Sigarda's Aid",
        // Legendary Land (PhyrexianTowerFactory).
        // {T}: Add {C}. {T}, Sacrifice a creature: Add {B}{B}.
        "Phyrexian Tower",
        // Creature — Human Soldier {1}{W} 2/2 (PuresteelPaladinFactory).
        // Whenever an Equipment enters under your control, you may draw a
        // card. Metalcraft — Equipment you control have equip {0} as long as
        // you control three or more artifacts.
        "Puresteel Paladin",
        // Artifact — {1} (AetherVialFactory). Darksteel.
        // Upkeep: you may put a charge counter on ~. {T}: put a creature
        // card with mana value equal to the number of charge counters on
        // ~ from your hand onto the battlefield.
        "Aether Vial",
        // Artifact — {0} (MoxOpalFactory). Metalcraft —
        // {T}: Add one mana of any color. Activate only if you control
        // three or more artifacts (CR 702.95).
        "Mox Opal",
        // Artifact Creature — Dragon 4/4 (ScionOfDracoFactory).
        // Domain cost reduction (CR 702.16) — costs {2} less to cast.
        // Multicolored creatures you control have flying, first strike,
        // vigilance, trample, and lifelink (subset wired per factory).
        "Scion of Draco",

        // ---- Modern staples shipped in the 2026-05-23d bump ----

        // Legendary Planeswalker — Wrenn {1}{R}{G} loyalty 3 (WrennAndRealmbreakerFactory).
        // +1 mill+land; -2 grave-reanimate; -7 emblem-tutor.
        "Wrenn and Realmbreaker",
        // Creature — Dauthi Rogue {B} 1/1 (DauthiVoidwalkerFactory).
        // Opponent-graveyard replace-with-exile + cast-from-exile.
        "Dauthi Voidwalker",
        // Instant — {R} (GalvanicDischargeFactory).
        // 1 damage + charge-counter scaling damage to any target.
        "Galvanic Discharge",
        // Land — (CavernOfSoulsFactory).
        // Choose-type ETB; {T}: Add {C} or any-color for chosen-type spells;
        // those spells can't be countered (CR 614).
        "Cavern of Souls",
        // Instant — {X}{G}{G}{G} (ChordOfCallingFactory).
        // Flash + convoke + creature tutor onto the battlefield (mv ≤ X).
        "Chord of Calling",
        // Sorcery — {2}{G} (EldritchEvolutionFactory).
        // Sac-creature additional cost; tutor creature of mv up to sacrificed +2.
        "Eldritch Evolution",
        // Artifact — {X}{X} (ChaliceOfTheVoidFactory).
        // ETB with X charge counters; symmetric counter-spell-of-MV-X (CR 614).
        "Chalice of the Void",
        // Creature — Human Scout {1}{G} 1/2 (TirelessTrackerFactory).
        // Landfall create Clue; sac-Clue draw + +1/+1 counter on ~.
        "Tireless Tracker",
        // Artifact — {X} (EngineeredExplosivesFactory).
        // ETB with X charge counters; {2}, Sac: destroy all nonland permanents
        // with MV equal to charge-counter count.
        "Engineered Explosives",
        // Sorcery — {R} (WrennsResolveFactory).
        // Draw 2; exile-EOT rider on the drawn cards.
        "Wrenn's Resolve",
        // Creature — Giant {2}{R} 4/3 (BonecrusherGiantFactory).
        // Targeted-by-spell trigger deals 2 damage to spell's controller.
        // Adventure deferred. DFC stored under composite name.
        "Bonecrusher Giant // Stomp",
        // Instant — {U} (SpellSnareFactory).
        // Counter target spell with mana value 2.
        "Spell Snare",
        // Instant — {(r/g)}{(r/g)} (ManamorphoseFactory).
        // Add 2 mana of any color combination; cantrip.
        "Manamorphose",
        // Creature — Illusion {U} 0/0 (PhantasmalImageFactory).
        // ETB as copy of any creature; sac trigger when targeted (Illusion).
        "Phantasmal Image",
        // Instant — {B} (CabalRitualFactory).
        // Add {B}{B}{B}; Threshold — adds {B}{B}{B}{B}{B} instead.
        "Cabal Ritual",
        // Sorcery — {R} (FaithlessLootingFactory).
        // Draw 2, discard 2; Flashback {2}{R}.
        "Faithless Looting",
        // Sorcery — {1}{R} (FaithlessSalvagingFactory).
        // Discard 1, then draw 1; Flashback—Discard a creature card.
        "Faithless Salvaging",
        // Instant — {G} (VeilOfSummerFactory).
        // Conditional draw + uncounterable + hexproof from UB this turn.
        "Veil of Summer",
        // Enchantment — {B}{B}{B} (NecropotenceFactory).
        // Skip draw step; end-of-turn discard-exile; pay 1 life set-aside-then-draw.
        "Necropotence",
        // Legendary Land — (KarakasFactory).
        // {T}: Add {W}; {T}: Bounce target legendary creature to owner's hand.
        "Karakas",
        // Enchantment — {1}{W} (StonySilenceFactory).
        // Global static — activated abilities of artifacts can't be activated
        // (mana abilities exempt, CR 605).
        "Stony Silence",

        // Instant — {U} (BrainstormTemplate, Bespoke).
        // "Draw three cards, then put two cards from your hand on top of
        // your library in any order." v1 deterministically returns the last
        // two hand cards; real "any order" player choice deferred.
        "Brainstorm",

        // ---- Modern staples shipped in the 2026-05-23e bump ----

        // Creature — Goblin {R} 1/1 (GoblinLackeyFactory).
        // Combat-damage trigger: cheat a Goblin from hand into play.
        "Goblin Lackey",
        // Instant — {R} (SearingBlazeFactory).
        // Landfall conditional twin damage (1 to player + 3 to creature
        // when landfall met; deals 1 + 3 split per oracle text).
        "Searing Blaze",
        // Artifact — {1}{B} (WishclawTalismanFactory). Throne of Eldraine.
        // {T}, Pay 3 life: search for a card → hand; opponent gains control.
        "Wishclaw Talisman",
        // Sorcery — {2}{B}{B}{B} (YawgmothsWillFactory). Urza's Saga.
        // Play cards from your graveyard this turn; cards that would be
        // put into your graveyard from anywhere are exiled instead.
        "Yawgmoth's Will",
        // Enchantment — {2}{R}{R} (ManabarbsFactory). Sixth Edition.
        // "Whenever a player taps a land for mana, Manabarbs deals 1
        // damage to that player."
        "Manabarbs",
        // Creature — Human Mercenary {1}{B} 2/2 (PlagueEngineerFactory).
        // ETB choose creature type; creatures of chosen type opponents
        // control get -1/-1.
        "Plague Engineer",
        // Legendary Artifact — {5} (PyromancersGogglesFactory). Magic Origins.
        // {T}: Add {R}. Copy-instant-or-sorcery-using-this-mana rider deferred.
        "Pyromancer's Goggles",
        // Instant — {1}{U} (DazeFactory). Nemesis.
        // Counter target spell unless its controller pays {1}.
        // Bounce-Island pitch alt-cost via BounceLandPitchAlternativeCost.
        "Daze",
        // Legendary Enchantment Creature — Nymph {G}{W} 1/2
        // (SythisHarvestsHandFactory). Modern Horizons 2.
        // Constellation — whenever an enchantment enters under your control,
        // gain 1 life; if it's not a token, also draw a card.
        "Sythis, Harvest's Hand",
        // Enchantment — Aura {2}{U}{R} (SplinterTwinFactory). Rise of the Eldrazi.
        // Enchant creature; enchanted creature has "{T}: Create a token that's
        // a copy of this creature, except it has haste. Exile token at end of turn."
        // Wired via AttachedAuraAbilityGrantStaticEffect.
        "Splinter Twin",
        // Sorcery — {U} (PonderFactory). Lorwyn / Modern Horizons 3.
        // Look at top 3, rearrange or shuffle, then draw.
        "Ponder",
        // Sorcery — {U} (PreordainFactory). Magic 2011 / Modern Horizons 3.
        // Scry 2, then draw a card.
        "Preordain",
        // Instant — {W} (PathToExileFactory). Conflux / Modern Horizons 3.
        // Exile target creature; that player may search library for a basic
        // land card and put it onto the battlefield tapped, then shuffle.
        "Path to Exile",
        // Artifact — Equipment {1} (SkullclampFactory). Darksteel.
        // Equipped creature gets +1/-1. Whenever equipped creature dies, draw
        // two cards. Equip {1}.
        "Skullclamp",
        // Legendary Artifact — Equipment {2} (UmezawasJitteFactory). Betrayers
        // of Kamigawa. Whenever equipped creature deals combat damage, put two
        // charge counters on Umezawa's Jitte. Remove a charge counter from
        // Umezawa's Jitte: Choose one — Jitte deals 2 damage to any target /
        // target creature gets -1/-1 EOT / you gain 2 life. Equip {2}.
        "Umezawa's Jitte",

        // ---- Modern staples shipped in the 2026-05-23f bump ----

        // Instant — {W} (SwordsToPlowsharesFactory). Alpha.
        // Exile target creature. Its controller gains life equal to its power.
        "Swords to Plowshares",
        // Instant — {U} (MysticalTutorFactory). Mirage.
        // Search your library for an instant or sorcery card, put it on top.
        "Mystical Tutor",
        // Land — (WastelandFactory). Tempest.
        // {T}: Add {C}. {T}, Sacrifice ~: Destroy target nonbasic land.
        "Wasteland",
        // Land — (MutavaultFactory). Morningtide.
        // {T}: Add {C}. {1}: ~ becomes a 2/2 creature with all creature types
        // until end of turn. It's still a land.
        "Mutavault",
        // Creature — Goblin {1}{R} 1/1 (GoblinMatronFactory). Urza's Saga.
        // ETB: search your library for a Goblin card, reveal it, put in hand.
        "Goblin Matron",
        // Land — (InkmothNexusFactory). Mirrodin Besieged.
        // {T}: Add {C}. {1}: ~ becomes a 1/1 Phyrexian Insect Artifact creature
        // with flying and infect until end of turn. It's still a land.
        "Inkmoth Nexus",
        // Creature — Spirit {1}{W}{U} 2/3 (SpellQuellerFactory). Eldritch Moon.
        // Flash + Flying. ETB exile target spell with mana value 4 or less.
        // When ~ leaves the battlefield, owner of exiled card may cast it
        // without paying its mana cost.
        "Spell Queller",
        // Legendary Artifact — {1} (SenseisDiviningTopFactory). Champions of
        // Kamigawa. {1}: look at top 3 of library, put back in any order.
        // {T}: Draw a card, then put ~ on top of owner's library.
        "Sensei's Divining Top",
        // Creature — Giant {4}{W}{W} 6/6 (SunTitanFactory). Magic 2011.
        // Vigilance. ETB and attack trigger: return target permanent card with
        // mana value 3 or less from your graveyard to the battlefield.
        "Sun Titan",
        // Creature — Human Wizard {2}{U} 2/2 (TrinketMageFactory). Fifth Dawn.
        // ETB: search library for an artifact card with mana value 1 or less,
        // reveal it, put in hand.
        "Trinket Mage",
        // Sorcery — {R} (RecklessChargeFactory). Odyssey.
        // Target creature gets +3/+0 and gains haste until end of turn.
        // Flashback {2}{R}.
        "Reckless Charge",
        // Artifact — Equipment {3} (SwordOfFireAndIceFactory). Darksteel.
        // Equipped creature gets +2/+2 and has protection from red and from
        // blue. Whenever equipped creature deals combat damage to a player,
        // ~ deals 2 damage to any target and you draw a card. Equip {2}.
        "Sword of Fire and Ice",
        // Creature — Human Artificer {1}{R} 1/1 (GoblinWelderFactory). Urza's
        // Legacy. {T}: Choose target artifact card in a graveyard and target
        // artifact a player controls. If both targets are still legal, that
        // player sacrifices the artifact they control, then returns the
        // artifact card from their graveyard to the battlefield.
        "Goblin Welder",
        // Land — (MishrasWorkshopFactory). Antiquities.
        // {T}: Add {C}{C}{C}. Spend this mana only to cast artifact spells.
        "Mishra's Workshop",
        // Creature — Human Warrior {R}{R}{W} 3/3 (BorosReckonerFactory).
        // Damage-redirect trigger (CR 614) — whenever ~ is dealt damage,
        // ~ deals that much damage to any target. Wired via
        // OracleTriggeredAbilityBinder + DealDamage; auto-targets defender.
        "Boros Reckoner",
        // Artifact — Equipment {3} (SwordOfFeastAndFamineFactory). Mirrodin.
        // Equipped creature gets +2/+2 and has protection from black and from
        // green. Whenever equipped creature deals combat damage to a player,
        // that player discards a card and you untap all lands you control.
        // Equip {2}.
        "Sword of Feast and Famine",
        // Instant — {R}{W} (LightningHelixFactory). Ravnica: City of Guilds.
        // ~ deals 3 damage to any target and you gain 3 life.
        "Lightning Helix",
        // Instant — {U} (BrainFreezeFactory). Scourge.
        // Storm (CR 702.40). Target player puts the top three cards of their
        // library into their graveyard.
        "Brain Freeze",

        // ---- Modern staples shipped in the 2026-05-23g bump ----

        // Sorcery — {2}{R} (WheelOfFortuneFactory). Limited Edition Alpha.
        // Each player discards their hand, then draws seven cards.
        "Wheel of Fortune",
        // Enchantment — {X}{B}{G} (PerniciousDeedFactory). Apocalypse.
        // Sacrifice ~: Destroy each artifact, creature, and enchantment with
        // mana value X or less. Mv-bounded sweeper.
        "Pernicious Deed",
        // Creature — Human Shaman {R} 1/1 (DragonsRageChannelerFactory).
        // Modern Horizons 2. Whenever you cast a noncreature spell, surveil 1.
        // Delirium — ~ gets +2/+2 and has flying as long as four or more card
        // types are among cards in your graveyard.
        "Dragon's Rage Channeler",
        // Sorcery — {B}{B} (TendrilsOfAgonyFactory). Scourge.
        // Storm (CR 702.40). Target opponent loses 2 life and you gain 2 life.
        "Tendrils of Agony",
        // Artifact — {1} (AetherSpellbombFactory). Mirrodin.
        // {U}, Sacrifice ~: Return target creature to its owner's hand.
        // {1}, Sacrifice ~: Draw a card.
        "Aether Spellbomb",
        // Artifact — {2} (CursedTotemFactory). Mirage.
        // Static (CR 605): Activated abilities of creatures can't be activated
        // unless they're mana abilities.
        "Cursed Totem",
        // Instant — {B} (VampiricTutorFactory). Visions.
        // Search your library for a card, put it on top of your library, then
        // shuffle. You lose 2 life.
        "Vampiric Tutor",
        // Instant — 0 (SlaughterPactFactory). Future Sight.
        // Destroy target nonblack creature. At the beginning of your next
        // upkeep, pay {2}{B}{B}. If you don't, you lose the game.
        "Slaughter Pact",
        // Instant — 0 (PactOfTheTitanFactory). Future Sight.
        // Create a 4/4 red Giant creature token. At the beginning of your
        // next upkeep, pay {4}{R}. If you don't, you lose the game.
        "Pact of the Titan",
        // Creature — Goblin Artificer {1}{R} 1/2 (GoblinEngineerFactory).
        // Modern Horizons. ETB tutor an artifact card from library to
        // graveyard. {T}, Sacrifice an artifact: return target artifact card
        // with mana value 3 or less from your graveyard to the battlefield.
        "Goblin Engineer",

        // ---- Modern staples shipped in the 2026-05-23h bump ----

        // Instant — {R} (BurstLightningFactory). Zendikar.
        // ~ deals 2 damage to any target. Kicker {4} — if kicked, ~ deals
        // 4 damage to that permanent or player instead (kicker deferred).
        "Burst Lightning",
        // Instant — {B} (DarkRitualFactory). Limited Edition Alpha.
        // Add {B}{B}{B}.
        "Dark Ritual",
        // Artifact — {0} (LotusPetalFactory). Tempest.
        // {T}, Sacrifice ~: Add one mana of any color.
        "Lotus Petal",
        // Artifact — {1} (SolRingFactory). Limited Edition Alpha.
        // {T}: Add {C}{C}.
        "Sol Ring",
        // Artifact — {1} (ManaVaultFactory). Limited Edition Alpha.
        // ~ doesn't untap during your untap step. At the beginning of your
        // upkeep, if ~ is tapped, you lose 1 life. {4}: untap ~.
        // {T}: Add {C}{C}{C}.
        "Mana Vault",
        // Sorcery — {B} (CabalTherapyFactory). Judgment.
        // Name a nonland card. Target player reveals their hand and discards
        // all copies of that card. Flashback — Sacrifice a creature.
        "Cabal Therapy",
        // Sorcery — {B} (ReanimateFactory). Urza's Saga.
        // Put target creature card from a graveyard onto the battlefield
        // under your control. You lose life equal to its mana value.
        "Reanimate",
        // Enchantment — Aura {1}{B} (AnimateDeadFactory). Limited Edition Alpha.
        // Enchant creature card in a graveyard. When ~ enters, if it's on
        // the battlefield, it loses "enchant creature card in a graveyard"
        // and gains "enchant creature put onto the battlefield with ~."
        // Return enchanted creature card to the battlefield under your
        // control and attach ~ to it. Enchanted creature gets -1/-0.
        "Animate Dead",
        // Sorcery — {2}{U}{U} (ShowAndTellFactory). Urza's Saga.
        // Each player may put an artifact, creature, enchantment, or land
        // card from their hand onto the battlefield.
        "Show and Tell",
        // Sorcery — {2}{R}{R} (ThroughTheBreachFactory). Champions of
        // Kamigawa. You may put a creature card from your hand onto the
        // battlefield. That creature gains haste until end of turn.
        // Sacrifice it at the beginning of the next end step. Splice onto
        // Arcane — Sacrifice a Mountain (splice deferred).
        "Through the Breach",
        // Enchantment — {2}{R}{R} (SneakAttackFactory). Urza's Saga.
        // {R}: You may put a creature card from your hand onto the
        // battlefield. That creature gains haste. Sacrifice it at the
        // beginning of the next end step.
        "Sneak Attack",
        // Legendary Creature — Phyrexian Praetor {2}{B}{B} 4/5
        // (SheoldredTheApocalypseFactory). Dominaria United.
        // Whenever you draw a card, you gain 1 life. Whenever an opponent
        // draws a card, they lose 1 life. Wired via OracleTriggeredAbilityBinder.
        "Sheoldred, the Apocalypse",
        // Legendary Artifact — {4} (TheOneRingFactory). The Lord of the Rings:
        // Tales of Middle-earth. Indestructible (cosmetic, deferred).
        // When ~ enters, if you cast it, you gain protection from everything
        // until your next turn. At the beginning of your upkeep, you put a
        // burden counter on ~. Whenever you put one or more burden counters
        // on ~, you lose that much life. {T}: draw a card for each burden
        // counter on ~.
        "The One Ring",
        // Legendary Creature — Phyrexian Angel {3}{G}{W}{U}{B} 7/7
        // (AtraxaGrandUnifierFactory). Phyrexia: All Will Be One.
        // Flying, vigilance, deathtouch, lifelink. When ~ enters, reveal
        // the top 10 cards of your library. Put one of each card type
        // (artifact, battle, creature, enchantment, instant, land,
        // planeswalker, sorcery) from among them into your hand. Put the
        // rest on the bottom of your library in a random order.
        "Atraxa, Grand Unifier",
        // Instant — {U}{B}{R} (ArchmagesCharmFactory). Modern Horizons.
        // Choose one — Target player draws two cards; or destroy target
        // nonland permanent with mana value 1 or less; or gain control of
        // target creature with mana value 2 or less.
        "Archmage's Charm",
        // Legendary Creature — Elder Giant {1}{G}{U} 6/6 (UroTitanFactory).
        // Theros Beyond Death. When ~ enters, sacrifice it unless it
        // escapes. Whenever ~ enters or attacks, you gain 3 life and draw
        // a card, then you may put a land card from your hand onto the
        // battlefield. Escape — {G}{G}{U}{U}, exile five other cards from
        // your graveyard (escape deferred).
        "Uro, Titan of Nature's Wrath",
        // Creature — Avatar Horror {7} */* (HogaakFactory). Modern Horizons.
        // ~ can't be cast. Convoke. Delve. You may cast ~ from your
        // graveyard by paying {G}{G}{B}{B} rather than paying its mana cost.
        // Trample. ~'s power and toughness are each equal to the number of
        // creature cards in your graveyard.
        "Hogaak, Arisen Necropolis",
        // Tribal Enchantment — Zombie {1}{B} (BridgeFromBelowFactory).
        // Future Sight. Whenever a nontoken creature is put into your
        // graveyard from the battlefield, if ~ is in your graveyard, create
        // a 2/2 black Zombie creature token. When a creature is put into an
        // opponent's graveyard from the battlefield, exile ~.
        "Bridge from Below",

        // ---- Modern staples shipped in the 2026-05-23i bump ----

        // Legendary Planeswalker — Oko {1}{G}{U} loyalty 4 (OkoThiefOfCrownsFactory).
        // Throne of Eldraine. +2: create a Food token. +1: target artifact
        // or creature becomes a 3/3 green Elk creature with no abilities.
        // -5: exchange control of target artifact/creature with a Food.
        "Oko, Thief of Crowns",
        // Sorcery — {X}{W} (PrismaticEndingFactory). Modern Horizons 2.
        // Exile target nonland permanent with mana value X or less. X can't
        // be greater than the number of colors of mana spent to cast this
        // spell (CR 107.4 + 700.2 — colors-spent cap).
        "Prismatic Ending",
        // Sorcery — {R} (HidetsugusSecondRiteFactory). Champions of
        // Kamigawa. ~ deals 10 damage to target opponent if that player has
        // exactly 10 life.
        "Hidetsugu's Second Rite",
        // Creature — Human Wizard {U}{R} 2/2 (GoblinElectromancerFactory).
        // Return to Ravnica. Instant and sorcery spells you cast cost {1}
        // less to cast (cost-reduction static).
        "Goblin Electromancer",
        // Artifact — {0} (ManaCryptFactory). Mirage / Eternal Masters.
        // At the beginning of your upkeep, flip a coin. If you lose the
        // flip, ~ deals 3 damage to you. {T}: Add {C}{C}.
        "Mana Crypt",
        // Legendary Artifact — {0} (MoxAmberFactory). Dominaria.
        // {T}: Add one mana of any color among legendary creatures and
        // legendary planeswalkers you control.
        "Mox Amber",
        // Creature — Frog {U}{B} 1/3 (PsychicFrogFactory). Modern Horizons 3.
        // Flying. Whenever ~ deals combat damage to a player, draw that many
        // cards, then discard a card. Discard a card: ~ gets +1/+1 until
        // end of turn.
        "Psychic Frog",
        // Legendary Artifact — Vehicle {2}{G}{G} 4/4 (EsikasChariotFactory).
        // Kaldheim. When ~ enters, create two 2/2 green Cat creature tokens.
        // Whenever ~ attacks, create a token that's a copy of a target token
        // you control. Crew 4.
        "Esika's Chariot",
        // Legendary Creature — Bird Spirit {1}{G}{U} 3/4 (NaduWingedWisdomFactory).
        // Modern Horizons 3. Flying. Whenever a creature you control becomes
        // the target of a spell or ability, that creature's controller
        // reveals the top card of their library. If it's a land, they put it
        // onto the battlefield; otherwise, into hand. Cap 2 reveals per
        // creature per turn.
        "Nadu, Winged Wisdom",
        // Legendary Creature — Cat Warrior {1}{W} 2/2 // Legendary Planeswalker
        // — Ajani (AjaniNacatlPariahFactory). DFC front face. Modern Horizons 3.
        // Whenever one or more other creatures you control die, you may
        // exile ~. If you do, transform ~. (Back-face Nacatl Avenger
        // planeswalker loyalty abilities deferred — no per-face cardpool.)
        "Ajani, Nacatl Pariah // Ajani, Nacatl Avenger",
        // Enchantment — {2}{B}{B} (TheMeathookMassacreFactory). Innistrad:
        // Midnight Hunt. When ~ enters, all creatures get -X/-X until end of
        // turn, where X is the number of mana spent to cast ~. Whenever a
        // creature dies, each opponent loses 1 life and you gain 1 life.
        "The Meathook Massacre",
        // Enchantment — {B}{B}{B} (NecrodominanceFactory). Modern Horizons 3.
        // Necropotence variant. If you would draw a card, skip that draw
        // instead. Skip your draw step. At the beginning of your end step,
        // discard the cards in your hand in excess of five. Pay 1 life:
        // Exile the top card of your library face down. Put that card into
        // your hand at the beginning of your next end step.
        "Necrodominance",

        // ---- Modern staples shipped in the 2026-05-23j bump ----

        // Legendary Planeswalker — Karn {4} loyalty 5 (KarnScionOfUrzaFactory).
        // Aether Revolt. +1: reveal-pile choose-one-to-hand pile builder.
        // -1: Construct artifact creature token whose P/T equals the number
        // of artifacts you control.
        "Karn, Scion of Urza",
        // Legendary Creature — Elder Giant {1}{R}{W} 4/4 (PhlageFactory).
        // Modern Horizons 3. Trample. When ~ enters or attacks, it deals 3
        // damage to any target and you gain 3 life. Escape — {2}{R}{W},
        // exile five other cards from your graveyard (escape deferred).
        "Phlage, Titan of Fire's Fury",
        // Sorcery — {2}{W}{W} (WrathOfGodFactory). Limited Edition Alpha.
        // Destroy all creatures. They can't be regenerated.
        "Wrath of God",
        // Sorcery — {2}{B}{B} (DamnationFactory). Planar Chaos.
        // Black functional reprint of Wrath of God — destroy all creatures.
        "Damnation",
        // Artifact Creature — Horror {2} 0/4 (SpellskiteFactory). New Phyrexia.
        // {Phyrexian/U}: Change the target of target spell or ability to ~
        // (CR 702.59 — phyrexian-mana redirect activation).
        "Spellskite",
        // Sorcery — {1}{R} (PyroclasmFactory). Many reprints.
        // ~ deals 2 damage to each creature.
        "Pyroclasm",
        // Sorcery — {2}{R} (AngerOfTheGodsFactory). Theros.
        // ~ deals 3 damage to each creature. If a creature dealt damage this
        // way would die this turn, exile it instead.
        "Anger of the Gods",
        // Creature — Phoenix {3}{R} 3/2 (ArclightPhoenixFactory). Guilds of
        // Ravnica. Flying, haste. At the beginning of combat on your turn,
        // if you've cast three or more instant and/or sorcery spells this
        // turn, you may return ~ from your graveyard to the battlefield.
        "Arclight Phoenix",
        // Instant — {U}{B} (DrownInTheLochFactory). Throne of Eldraine.
        // Modal — counter target spell with mana value X or less; or destroy
        // target creature or planeswalker with mana value X or less, where X
        // is the number of cards in target opponent's graveyard.
        "Drown in the Loch",
        // Sorcery — {2}{G}{G} (ScapeshiftFactory). Morningtide.
        // Sacrifice any number of lands. Search your library for that many
        // land cards, put them onto the battlefield tapped, then shuffle.
        "Scapeshift",
        // Creature — Goblin {1}{R} 2/2 (GoblinChieftainFactory). Magic 2010.
        // Other Goblin creatures you control get +1/+1 and have haste.
        "Goblin Chieftain",
        // Creature — Goblin {1}{R}{R} 2/2 (GoblinWarchiefFactory). Onslaught /
        // Scourge / Modern Horizons. Goblin spells you cast cost {1} less to
        // cast. Goblin creatures you control have haste.
        "Goblin Warchief",
        // Creature — Goblin Warrior {1}{R} 1/2 (GoblinPiledriverFactory).
        // Onslaught / Modern Horizons. Protection from blue. Whenever ~
        // attacks, it gets +2/+0 until end of turn for each other attacking
        // Goblin.
        "Goblin Piledriver",
        // Creature — Zombie Knight {1}{B}{B} 2/3 // Instant — Adventure (MurderousRiderFactory).
        // Throne of Eldraine. DFC stored under composite name. Lifelink on
        // the creature face. Adventure half "Swift End" — {1}{B}{B}, destroy
        // target creature or planeswalker, you lose 2 life — exposed via
        // MurderousRiderFactory.BuildAdventureSpell.
        "Murderous Rider // Swift End",
    };
}
