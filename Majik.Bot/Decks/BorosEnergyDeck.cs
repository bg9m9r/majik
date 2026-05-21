namespace Majik.Bot.Decks;

/// <summary>
/// Boros Energy archetype — Modern-style midrange with energy counters,
/// value creatures, and creature-burn. Sideboard is NOT wired in v1
/// (BotDeckCatalog returns a single 60-card list per archetype). The
/// 15-card sideboard captured in plan docs for future use.
///
/// Many cards in this list are not yet IsImplemented=true in cards.db;
/// BotDeckValidator logs warnings at startup. Until those cards are
/// implemented, the engine treats unknown cards as vanilla — bot play
/// is best-effort against missing implementations.
/// </summary>
internal static class BorosEnergyDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures + planeswalkers (22)
        "Ajani, Nacatl Pariah", "Ajani, Nacatl Pariah", "Ajani, Nacatl Pariah", "Ajani, Nacatl Pariah",
        "Boromir, Warden of the Tower",
        "Guide of Souls", "Guide of Souls", "Guide of Souls", "Guide of Souls",
        "Ocelot Pride", "Ocelot Pride", "Ocelot Pride", "Ocelot Pride",
        "Ragavan, Nimble Pilferer", "Ragavan, Nimble Pilferer", "Ragavan, Nimble Pilferer",
        "Ranger-Captain of Eos",
        "Seasoned Pyromancer", "Seasoned Pyromancer",
        "Solitude",
        "Voice of Victory", "Voice of Victory",

        // Spells / enchantments / sagas (16)
        "Fable of the Mirror-Breaker", "Fable of the Mirror-Breaker",
        "Galvanic Discharge", "Galvanic Discharge", "Galvanic Discharge", "Galvanic Discharge",
        "Goblin Bombardment", "Goblin Bombardment", "Goblin Bombardment",
        "Mana Tithe", "Mana Tithe",
        "Blood Moon",
        "Static Prison",
        "The Legend of Roku",
        "Thraben Charm", "Thraben Charm",

        // Lands (22)
        "Arena of Glory",
        "Arid Mesa", "Arid Mesa", "Arid Mesa", "Arid Mesa",
        "Dalkovan Encampment",
        "Elegant Parlor", "Elegant Parlor",
        "Flooded Strand", "Flooded Strand", "Flooded Strand",
        "Marsh Flats", "Marsh Flats", "Marsh Flats",
        "Mountain",
        "Plains", "Plains",
        "Sacred Foundry", "Sacred Foundry", "Sacred Foundry",
        "Windswept Heath", "Windswept Heath",
    };
}
