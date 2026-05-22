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
        "Fatal Push", "Thoughtseize",
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
    };
}
