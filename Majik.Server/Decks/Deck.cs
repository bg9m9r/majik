using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Majik.Server.Decks;

public sealed class Deck
{
    [BsonId] public ObjectId InternalId { get; set; }

    [BsonElement("id"), BsonRepresentation(BsonType.String)]
    public required Guid Id { get; init; }

    [BsonElement("ownerSub")]
    public required string OwnerSub { get; init; }

    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("mainboard")]
    public required List<DeckCardEntry> Mainboard { get; set; }

    [BsonElement("sideboard")]
    public required List<DeckCardEntry> Sideboard { get; set; }

    [BsonElement("createdAt")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public sealed class DeckCardEntry
{
    [BsonElement("name")] public required string Name { get; init; }
    [BsonElement("count")] public required int Count { get; init; }
}
