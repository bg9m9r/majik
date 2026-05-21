using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Majik.Core.CardData.Database;

/// <summary>
/// EF Core connection interceptor that applies performance-oriented PRAGMAs
/// to every SQLite connection as it opens.
///
/// Why: the SQLite defaults are conservative — cache_size = 2 MB and
/// mmap_size = 0. With a 550 MB card database those defaults mean cold
/// queries hit physical disk on every page miss. WAL is already enabled
/// (we verified this on prod via SSH), but caching is the next win.
///
/// PRAGMAs set:
/// - cache_size = -50000 — 50 MB per-connection page cache (negative ⇒ KiB).
/// - mmap_size = 268435456 — 256 MB memory-mapped I/O so the kernel can
///   cache hot pages of the DB across connections.
/// - temp_store = MEMORY — keep sort/group/distinct scratch in RAM rather
///   than spilling to disk.
///
/// These values are sized for the 512 MB container Render's Starter tier
/// provides: the 50 MB cache + 256 MB mmap window comfortably fit alongside
/// the .NET runtime + game state.
/// </summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Apply(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    private static void Apply(DbConnection connection)
    {
        if (connection is not SqliteConnection) return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA cache_size = -50000;
            PRAGMA mmap_size = 268435456;
            PRAGMA temp_store = MEMORY;
        """;
        cmd.ExecuteNonQuery();
    }
}
