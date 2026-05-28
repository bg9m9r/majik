namespace Majik.Bot.Decks;

/// <summary>
/// Living End archetype — Modern cycling-reanimator combo. Pitch big
/// creatures (Striped Riverwinder, Waker of Waves, Curator of Mysteries,
/// Colossal Skyturtle, Street Wraith, Architects of Will) into the
/// graveyard via cycling, then cascade Violent Outburst / Shardless
/// Agent into Living End to mass-reanimate the team. Subtlety / Endurance
/// / Force of Negation / Foundation Breaker for free disruption.
/// Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (Living End, current snapshot).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class LivingEndDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        // Creatures (24)
        "Striped Riverwinder", "Striped Riverwinder", "Striped Riverwinder", "Striped Riverwinder",
        "Curator of Mysteries", "Curator of Mysteries",
        "Architects of Will", "Architects of Will", "Architects of Will", "Architects of Will",
        "Street Wraith", "Street Wraith", "Street Wraith", "Street Wraith",
        "Waker of Waves", "Waker of Waves", "Waker of Waves", "Waker of Waves",
        "Colossal Skyturtle", "Colossal Skyturtle",
        "Subtlety", "Subtlety",
        "Endurance", "Endurance",

        // Spells (17)
        "Living End", "Living End", "Living End", "Living End",
        "Violent Outburst", "Violent Outburst", "Violent Outburst", "Violent Outburst",
        "Shardless Agent", "Shardless Agent", "Shardless Agent", "Shardless Agent",
        "Force of Negation", "Force of Negation",
        "Foundation Breaker", "Foundation Breaker",
        "Brazen Borrower",

        // Lands (19)
        "Verdant Catacombs", "Verdant Catacombs", "Verdant Catacombs", "Verdant Catacombs",
        "Misty Rainforest", "Misty Rainforest",
        "Wooded Foothills", "Wooded Foothills",
        "Steam Vents",
        "Stomping Ground",
        "Breeding Pool",
        "Forest", "Forest",
        "Island",
        "Botanical Sanctum",
        "Spara's Headquarters",
        "Ziatora's Proving Ground",
        "Fiery Islet",
        "Waterlogged Grove",
    };
}
