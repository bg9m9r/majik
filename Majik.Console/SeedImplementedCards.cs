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
    };
}
