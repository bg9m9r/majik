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
    };
}
