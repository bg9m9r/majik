namespace Majik.Core.Events;

/// <summary>
/// CR 701.20a — published whenever a player's library is shuffled
/// (search effects, "shuffle your library" riders, initial game start).
///
/// Replay-capability surface: a recorder can persist this event +
/// the deterministic <see cref="Majik.Core.Random.GameRandom"/> seed
/// to reproduce shuffle outcomes.
/// </summary>
public class LibraryShuffledEvent : GameEvent
{
    /// <summary>The player whose library was shuffled.</summary>
    public Players.Player Player { get; }

    /// <summary>
    /// Short reason tag for the shuffle. Human-readable; not parsed by
    /// the engine. Examples: "search", "mystical-tutor", "game-start",
    /// "graveyard-into-library".
    /// </summary>
    public string Reason { get; }

    /// <summary>Library card count at the moment the shuffle ran.</summary>
    public int CardCount { get; }

    public LibraryShuffledEvent(Players.Player player, string reason, int cardCount)
        : base(EventType.LibraryShuffled)
    {
        Player = player;
        Reason = reason;
        CardCount = cardCount;
    }
}
