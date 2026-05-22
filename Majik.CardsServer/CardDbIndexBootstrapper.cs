using Majik.Core.CardData.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Majik.CardsServer;

/// <summary>
/// Hosted service that runs once at startup to ensure perf-critical
/// indexes exist on the cards database. Idempotent — CREATE INDEX IF NOT
/// EXISTS makes subsequent boots a no-op (~1 ms).
///
/// Moved from Majik.Server when the cards service was extracted.
/// See the original docstring there for the NOCASE-collation reasoning.
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
            _logger.LogError(ex, "CardDbIndexBootstrapper failed to ensure NOCASE index");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
