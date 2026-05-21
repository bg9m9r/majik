using Majik.Core.CardData.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Majik.Server.Cards;

/// <summary>
/// Hosted service that runs once at server startup to ensure perf-critical
/// indexes exist on the cards database. Idempotent — CREATE INDEX IF NOT
/// EXISTS makes subsequent boots a no-op (~1 ms).
///
/// Why a NOCASE index on Name: SQLite's LIKE optimizer can only use an
/// index for prefix matches when the column's collation matches the LIKE
/// case sensitivity. Default LIKE is case-insensitive and the existing
/// IX_Cards_Name uses BINARY collation, so even <c>LIKE 'Forest%'</c>
/// falls back to a full table scan (measured: 2.4s cold on Render
/// Starter). A parallel NOCASE-collated index fixes that without breaking
/// any other query.
///
/// First-boot cost on prod (522k rows): ~5–15s on Starter. The index then
/// persists in cards.db so future boots skip the build.
/// </summary>
public sealed class CardDbIndexBootstrapper : IHostedService
{
    private readonly ILogger<CardDbIndexBootstrapper> _logger;

    public CardDbIndexBootstrapper(ILogger<CardDbIndexBootstrapper> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var db = new CardDbContext();
            var conn = db.Database.GetDbConnection();
            if (conn is not SqliteConnection sqlite) return Task.CompletedTask;

            sqlite.Open();
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                "CREATE INDEX IF NOT EXISTS IX_Cards_Name_NoCase ON Cards(Name COLLATE NOCASE);";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            cmd.ExecuteNonQuery();
            sw.Stop();
            _logger.LogInformation(
                "CardDbIndexBootstrapper: IX_Cards_Name_NoCase ready in {Ms} ms",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Don't block startup — search will still work, just slow.
            _logger.LogError(ex, "CardDbIndexBootstrapper failed to ensure NOCASE index");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
