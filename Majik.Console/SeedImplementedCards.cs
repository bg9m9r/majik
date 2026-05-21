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
    };
}
