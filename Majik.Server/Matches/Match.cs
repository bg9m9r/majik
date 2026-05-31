using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Majik.Server.Matches;

public enum MatchState { Open, Joined, Starting, Rolling, Playing, Completed, Abandoned }
public enum MatchVisibility { Public, Invite }

/// <summary>
/// One game between two players, from lobby post through completion.
/// Holds the lifecycle state machine, both players (with chosen deck IDs),
/// the dice roll, the engine game reference, and the chess-clock balances.
/// </summary>
public sealed class Match
{
    [BsonId] public ObjectId InternalId { get; set; }

    [BsonElement("id")]
    [BsonRepresentation(BsonType.String)]
    public required Guid Id { get; init; }

    [BsonElement("state")]
    [BsonRepresentation(BsonType.String)]
    public required MatchState State { get; set; }

    [BsonElement("visibility")]
    [BsonRepresentation(BsonType.String)]
    public required MatchVisibility Visibility { get; init; }

    [BsonElement("format")]
    public required string Format { get; init; }

    [BsonElement("clockMinutes")]
    public required int ClockMinutes { get; init; }

    [BsonElement("creator")]
    public required MatchPlayer Creator { get; init; }

    [BsonElement("opponent")]
    public MatchPlayer? Opponent { get; set; }

    [BsonElement("roll")]
    public MatchRoll? Roll { get; set; }

    [BsonElement("firstChoice")]
    public string? FirstChoice { get; set; }

    [BsonElement("gameId")]
    [BsonRepresentation(BsonType.String)]
    public Guid? GameId { get; set; }

    /// <summary>
    /// Determinism (PLAN 08 prerequisite): the RNG seed pinned at match
    /// creation and stored here. The engine's <c>GameRandom(seed)</c> is built
    /// from this value when the game boots, so the (seed, command order) pair
    /// is reproducible. Persisting the seed is the prerequisite that makes
    /// later command-log replay / replica rehydration (PLAN 08 body) possible;
    /// the durable command log + rehydration constructor remain out of scope.
    /// <para>Not <c>required</c> so a Match doc persisted before this field
    /// existed still deserializes (it reads back as 0); every code path that
    /// CREATES a match sets it explicitly via <c>NewGameSeed()</c>.</para>
    /// </summary>
    [BsonElement("gameSeed")]
    public int GameSeed { get; init; }

    [BsonElement("creatorMillisRemaining")]
    public long CreatorMillisRemaining { get; set; }

    [BsonElement("opponentMillisRemaining")]
    public long OpponentMillisRemaining { get; set; }

    [BsonElement("priorityHolderSub")]
    public string? PriorityHolderSub { get; set; }

    [BsonElement("priorityStartedAt")]
    public DateTime? PriorityStartedAt { get; set; }

    [BsonElement("winnerSub")]
    public string? WinnerSub { get; set; }

    [BsonElement("timeoutLoserSub")]
    public string? TimeoutLoserSub { get; set; }

    [BsonElement("createdAt")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public sealed class MatchPlayer
{
    [BsonElement("sub")] public required string Sub { get; init; }
    [BsonElement("handle")] public required string Handle { get; init; }
    [BsonElement("deckId")] public required string DeckId { get; init; }
    [BsonElement("deckSnapshot")] public required List<string> DeckSnapshot { get; init; }
}

public sealed class MatchRoll
{
    [BsonElement("creatorRoll")] public int? CreatorRoll { get; set; }
    [BsonElement("opponentRoll")] public int? OpponentRoll { get; set; }
    [BsonElement("winnerSub")] public string? WinnerSub { get; set; }
}
