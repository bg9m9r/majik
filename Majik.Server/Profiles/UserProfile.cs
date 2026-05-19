using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Majik.Server.Profiles;

/// <summary>
/// Per-user profile keyed by Descope JWT `sub`. Holds the user's chosen
/// display handle. The handle is stored twice: lowercased <see cref="Handle"/>
/// for the unique index + case-insensitive lookup, and <see cref="HandleDisplay"/>
/// preserving the original casing for UI rendering.
/// </summary>
public sealed class UserProfile
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("sub")]
    public required string Sub { get; init; }

    [BsonElement("handle")]
    public required string Handle { get; set; }

    [BsonElement("handleDisplay")]
    public required string HandleDisplay { get; set; }

    [BsonElement("createdAt")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
