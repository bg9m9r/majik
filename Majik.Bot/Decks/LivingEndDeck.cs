namespace Majik.Bot.Decks;

/// <summary>
/// Living End archetype — Modern cycling-cascade combo. Pitch cyclers
/// (Street Wraith, Generous Ent, Curator of Mysteries, Waker of Waves,
/// Wistfulness, Colossal Skyturtle) into the graveyard, then cascade
/// Violent Outburst / Shardless Agent into Living End to mass-reanimate.
/// Subtlety / Endurance / Force of Negation provide free disruption.
/// Sideboard NOT wired in v1.
///
/// Source: MTGGoldfish Modern metagame (verified 2026-05 against current
/// archetype top-3 mainboards; representative list snapshotted).
///
/// Many cards in this list are not yet IsImplemented=true; BotDeckValidator
/// logs warnings at startup. Engine treats unknown cards as vanilla until
/// each gets a binding.
/// </summary>
internal static class LivingEndDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Breeding Pool",
        "Colossal Skyturtle", "Colossal Skyturtle",
        "Commercial District",
        "Curator of Mysteries", "Curator of Mysteries", "Curator of Mysteries", "Curator of Mysteries",
        "Endurance", "Endurance", "Endurance", "Endurance",
        "Force of Negation", "Force of Negation", "Force of Negation", "Force of Negation",
        "Forest",
        "Generous Ent", "Generous Ent", "Generous Ent", "Generous Ent",
        "Hedge Maze",
        "Island",
        "Living End", "Living End", "Living End",
        "Mistrise Village",
        "Misty Rainforest", "Misty Rainforest", "Misty Rainforest", "Misty Rainforest",
        "Oliphaunt",
        "Otawara, Soaring City",
        "Scalding Tarn",
        "Shardless Agent", "Shardless Agent", "Shardless Agent", "Shardless Agent",
        "Sink into Stupor", "Sink into Stupor",
        "Steam Vents",
        "Stomping Ground",
        "Street Wraith", "Street Wraith", "Street Wraith", "Street Wraith",
        "Subtlety", "Subtlety", "Subtlety", "Subtlety",
        "Thundering Falls",
        "Violent Outburst", "Violent Outburst", "Violent Outburst", "Violent Outburst",
        "Waker of Waves",
        "Wistfulness", "Wistfulness", "Wistfulness", "Wistfulness",
    };
}
