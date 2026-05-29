namespace Majik.Bot.Decks;

/// <summary>
/// Azorius Blink archetype — Modern UW flicker value. Eldrazi Displacer +
/// Flickerwisp + Reflector Mage blink the Wall of Omens / Thought-Knot Seer
/// / Spellstutter Sprite ETB engine; Thalia + Path keep the board honest.
/// Aether Vial deploys instants' worth of bodies at flash speed. Sideboard
/// NOT wired in v1.
///
/// Source: mtgtop8 — Caleb Horowitz, Azorius Blink, SCG CON Richmond
/// (2026-03); seed-validated against the embedded pool (all 60 resolve).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class AzoriusBlinkDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (28)
        "Eldrazi Displacer", "Eldrazi Displacer", "Eldrazi Displacer", "Eldrazi Displacer",
        "Flickerwisp", "Flickerwisp", "Flickerwisp",
        "Kitchen Finks",
        "Reflector Mage", "Reflector Mage", "Reflector Mage",
        "Spellstutter Sprite", "Spellstutter Sprite", "Spellstutter Sprite", "Spellstutter Sprite",
        "Thalia, Guardian of Thraben", "Thalia, Guardian of Thraben", "Thalia, Guardian of Thraben",
        "Thought-Knot Seer", "Thought-Knot Seer", "Thought-Knot Seer", "Thought-Knot Seer",
        "Vendilion Clique",
        "Venser, Shaper Savant",
        "Wall of Omens", "Wall of Omens", "Wall of Omens", "Wall of Omens",

        // Spells (9)
        "Path to Exile", "Path to Exile", "Path to Exile", "Path to Exile",
        "Aether Vial", "Aether Vial", "Aether Vial", "Aether Vial",
        "Relic of Progenitus",

        // Lands (23)
        "Adarkar Wastes", "Adarkar Wastes", "Adarkar Wastes", "Adarkar Wastes",
        "Celestial Colonnade",
        "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple", "Eldrazi Temple",
        "Flooded Strand",
        "Hallowed Fountain", "Hallowed Fountain",
        "Island",
        "Mutavault", "Mutavault", "Mutavault", "Mutavault",
        "Plains", "Plains", "Plains",
        "Seachrome Coast", "Seachrome Coast", "Seachrome Coast",
    };
}
