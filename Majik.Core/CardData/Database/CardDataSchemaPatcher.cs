using System.Data.Common;

namespace Majik.Core.CardData.Database;

/// <summary>Idempotent SQLite schema patcher. Adds the
/// <c>IsImplemented</c> column to the existing <c>Cards</c> table if it
/// isn't already present. Safe to call on every startup.</summary>
public static class CardDataSchemaPatcher
{
    public static async Task PatchAsync(DbConnection conn, CancellationToken ct)
    {
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        var present = await ColumnExistsAsync(conn, table: "Cards", column: "IsImplemented", ct);
        if (present) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "ALTER TABLE Cards ADD COLUMN IsImplemented INTEGER NOT NULL DEFAULT 0";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection conn, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $col";
        var p = cmd.CreateParameter();
        p.ParameterName = "$col";
        p.Value = column;
        cmd.Parameters.Add(p);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }
}
