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
        "Plains", "Island", "Swamp", "Mountain", "Forest",
        // Vanilla creatures
        "Grizzly Bears", "Hill Giant", "Bear Cub", "Savannah Lions",
        "Goblin Piker", "Walking Corpse", "Phantom Warrior",
        "Llanowar Elves", "Runeclaw Bear", "Centaur Courser",
    };
}
