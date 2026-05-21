using System.Data.Common;

namespace Majik.Core.CardData.Database;

/// <summary>Idempotent SQLite schema patcher. Adds late-introduced columns
/// and tables to an existing user database so old DBs don't need to be
/// deleted and re-imported. Safe to call on every startup.</summary>
public static class CardDataSchemaPatcher
{
    public static async Task PatchAsync(DbConnection conn, CancellationToken ct)
    {
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        if (!await ColumnExistsAsync(conn, table: "Cards", column: "IsImplemented", ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE Cards ADD COLUMN IsImplemented INTEGER NOT NULL DEFAULT 0";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await EnsureCompiledSpellTemplatesTableAsync(conn, ct);
    }

    /// <summary>
    /// Idempotent <c>CREATE TABLE IF NOT EXISTS</c> for the
    /// <c>CompiledSpellTemplates</c> table (Phase 2 spell-template
    /// pre-compile pipeline). EF Core's <c>EnsureCreated</c> only runs once
    /// per DB, so this patcher backfills the table on existing user
    /// databases without forcing them to delete and re-import.
    /// </summary>
    private static async Task EnsureCompiledSpellTemplatesTableAsync(DbConnection conn, CancellationToken ct)
    {
        await using (var create = conn.CreateCommand())
        {
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS CompiledSpellTemplates (
                    CardName     TEXT NOT NULL PRIMARY KEY,
                    TemplateName TEXT NOT NULL,
                    Priority     INTEGER NOT NULL DEFAULT 0,
                    ParamsJson   TEXT NOT NULL DEFAULT '{}',
                    CompiledAt   INTEGER NOT NULL DEFAULT 0
                );";
            await create.ExecuteNonQueryAsync(ct);
        }

        await using (var index = conn.CreateCommand())
        {
            index.CommandText =
                "CREATE INDEX IF NOT EXISTS IX_CompiledSpellTemplates_TemplateName " +
                "ON CompiledSpellTemplates(TemplateName);";
            await index.ExecuteNonQueryAsync(ct);
        }
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
