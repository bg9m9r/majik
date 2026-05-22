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
        // Instant — {R}. DamageAnyTargetTemplate. Flashback (sac Mountain to
        // recast from graveyard) deferred — flashback infra missing.
        "Lava Dart",
        // Sorcery — {2}{R} (spectacle {R}). DamageAnyTargetTemplate handles
        // the 3-damage body. Spectacle alt-cost ("if an opponent lost life
        // this turn") deferred — needs alt-cost framework.
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
    };
}
