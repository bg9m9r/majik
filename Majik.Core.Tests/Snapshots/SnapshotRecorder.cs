using System.Text.Json;
using Majik.Core.CardData;

namespace Majik.Core.Tests.Snapshots;

/// <summary>
/// Helpers for the <c>SNAPSHOT_RECORD=1</c> path. Looks card-data rows
/// up in the embedded Modern-legal pool (shipped inside Majik.Core) and
/// writes them out to disk so the test runner can rebuild snapshots
/// against deterministic bundled fixtures.
///
/// Previously this hit the local SQLite cards.db; with the DB removed,
/// the same recording flow works against the embedded
/// <see cref="EmbeddedCardRepository"/> with no filesystem dependency.
/// </summary>
internal static class SnapshotRecorder
{
    private static readonly Lazy<EmbeddedCardRepository> _repo = new(
        () => new EmbeddedCardRepository(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The seed is always loadable in-process, so card data is
    /// always available for snapshot regeneration.</summary>
    public static bool IsLocalDbAvailable() => true;

    /// <summary>
    /// Look the card up in the embedded pool, write its
    /// <see cref="CardEntity"/> to disk under <c>card-data/&lt;slug&gt;.json</c>,
    /// and return true. Returns false if the card isn't in the pool —
    /// the caller should skip generating a snapshot for it.
    /// </summary>
    public static bool RefreshCardData(string cardName)
    {
        var entity = _repo.Value.GetByName(cardName);
        if (entity is null) return false;

        var path = Path.Combine(SnapshotPaths.CardDataDir,
            SnapshotPaths.Slug(cardName) + ".json");
        Directory.CreateDirectory(SnapshotPaths.CardDataDir);

        // CardEntity is already pruned to gameplay-relevant fields; the
        // embedded pool doesn't carry the volatile bookkeeping
        // (Id, ImportedAt, ImageUri, FormatLegalities) that the previous
        // recorder had to strip by hand, so this is a straight write.
        var json = JsonSerializer.Serialize(entity, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + "\n";
        File.WriteAllText(path, json);
        return true;
    }
}
