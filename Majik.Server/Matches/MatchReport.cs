using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Majik.Server.Matches;

/// <summary>Status lifecycle of a reported issue. Slice 1 only sets
/// <see cref="ReportStatus.IssueOpen"/>; later slices advance it.</summary>
public enum ReportStatus { Reported, IssueOpen, PrOpen, Merged, Delivered, NeedsHuman }

/// <summary>Light status record for an in-app issue report. The heavy
/// diagnostic bundle lives in the GitHub issue body, not here.</summary>
public sealed class MatchReport
{
    [BsonId] public ObjectId InternalId { get; set; }

    [BsonElement("id")]
    [BsonRepresentation(BsonType.String)]
    public required Guid Id { get; init; }

    [BsonElement("reporterSub")]
    public required string ReporterSub { get; init; }

    [BsonElement("matchId")]
    [BsonRepresentation(BsonType.String)]
    public required Guid MatchId { get; init; }

    [BsonElement("gameId")]
    [BsonRepresentation(BsonType.String)]
    public Guid? GameId { get; init; }

    [BsonElement("issueNumber")]
    public int? IssueNumber { get; set; }

    [BsonElement("issueUrl")]
    public string? IssueUrl { get; set; }

    [BsonElement("repo")]
    public string? Repo { get; set; }

    /// <summary>When the fix PR merged (set by the GitHub webhook on the
    /// Merged transition). Null until merged. The DeploymentWatcher compares
    /// this against the affected service's live build time to decide when to
    /// flip Merged → Delivered.</summary>
    [BsonElement("fixMergedAt")]
    public DateTime? FixMergedAt { get; set; }

    [BsonElement("title")]
    public required string Title { get; init; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public required ReportStatus Status { get; set; }

    [BsonElement("createdAt")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
