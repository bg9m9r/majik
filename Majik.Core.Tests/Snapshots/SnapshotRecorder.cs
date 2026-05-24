using System.Text.Json;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;

namespace Majik.Core.Tests.Snapshots;

/// <summary>
/// Helpers for the <c>SNAPSHOT_RECORD=1</c> path. Reads the user-installed
/// Scryfall SQLite DB once per process, exports the requested
/// <see cref="CardEntity"/> rows to JSON under
/// <c>Snapshots/card-data/&lt;slug&gt;.json</c>, then lets the test runner
/// rebuild the snapshot from those bundled rows.
///
/// Intentionally isolated from <see cref="ScryfallCardFactorySnapshotTests"/>
/// so the test class itself stays DB-free at runtime — only the recording
/// path opens <c>cards.db</c>, and only when explicitly opted into.
/// </summary>
internal static class SnapshotRecorder
{
    private static readonly object _gate = new();
    private static DbCardRepository? _liveRepo;
    private static bool _initialised;
    private static bool _dbAvailable;

    private static string LocalDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Majik", "cards.db");

    public static bool IsLocalDbAvailable()
    {
        EnsureInitialised();
        return _dbAvailable;
    }

    /// <summary>
    /// Look the card up in the live DB, write its
    /// <see cref="CardEntity"/> to disk under <c>card-data/&lt;slug&gt;.json</c>,
    /// and return true. Returns false if the local DB lacks the card — the
    /// caller should skip generating a snapshot for it.
    /// </summary>
    public static bool RefreshCardData(string cardName)
    {
        if (!IsLocalDbAvailable()) return false;
        var entity = _liveRepo!.GetByName(cardName);
        if (entity is null) return false;

        var path = Path.Combine(SnapshotPaths.CardDataDir,
            SnapshotPaths.Slug(cardName) + ".json");
        Directory.CreateDirectory(SnapshotPaths.CardDataDir);

        // Strip volatile bookkeeping that would otherwise rotate every time
        // the bulk-importer runs. The factory pipeline reads exactly the
        // fields the binders care about — name, mana cost, type line, oracle
        // text, P/T, loyalty, keywords. Pin everything else to deterministic
        // defaults so the bundled fixture stays diff-friendly.
        var stable = StripVolatileFields(entity);

        var json = JsonSerializer.Serialize(stable, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + "\n";
        File.WriteAllText(path, json);
        return true;
    }

    private static CardEntity StripVolatileFields(CardEntity src) => new()
    {
        // Id is the autoincrement PK — never part of the snapshot identity.
        Id = 0,
        ScryfallId = src.ScryfallId,
        Name = src.Name,
        ManaCost = src.ManaCost,
        Cmc = src.Cmc,
        TypeLine = src.TypeLine,
        OracleText = src.OracleText,
        Power = src.Power,
        Toughness = src.Toughness,
        Loyalty = src.Loyalty,
        Colors = src.Colors,
        ColorIdentity = src.ColorIdentity,
        Keywords = src.Keywords,
        Set = src.Set,
        CollectorNumber = src.CollectorNumber,
        Rarity = src.Rarity,
        ImageUri = null, // varies between printings; not factory-relevant
        Legalities = src.Legalities,
        ImportedAt = DateTime.MinValue,
        UpdatedAt = null,
        IsImplemented = src.IsImplemented,
        FormatLegalities = new(),
    };

    private static void EnsureInitialised()
    {
        if (_initialised) return;
        lock (_gate)
        {
            if (_initialised) return;
            try
            {
                if (File.Exists(LocalDbPath))
                {
                    using var probe = new Microsoft.Data.Sqlite.SqliteConnection(
                        $"Data Source={LocalDbPath};Mode=ReadOnly");
                    probe.Open();
                    using var cmd = probe.CreateCommand();
                    cmd.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Cards'";
                    if (Convert.ToInt32(cmd.ExecuteScalar()) == 1)
                    {
                        _liveRepo = new DbCardRepository(() => new CardDbContext());
                        _dbAvailable = true;
                    }
                }
            }
            catch
            {
                _dbAvailable = false;
                _liveRepo = null;
            }
            _initialised = true;
        }
    }
}
