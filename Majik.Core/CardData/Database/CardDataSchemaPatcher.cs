using System.Data.Common;
using System.Text.Json;

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
        await EnsureCardLegalitiesTableAsync(conn, ct);
    }

    /// <summary>
    /// Idempotent creation of the normalized <c>CardLegalities</c> side table
    /// (one row per <c>(Card, Format)</c>). Backfills from each card's
    /// <see cref="CardEntity.Legalities"/> JSON blob on first patch — subsequent
    /// imports populate rows directly via <c>ScryfallJsonImporter</c>.
    /// </summary>
    private static async Task EnsureCardLegalitiesTableAsync(DbConnection conn, CancellationToken ct)
    {
        await using (var create = conn.CreateCommand())
        {
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS CardLegalities (
                    CardId INTEGER NOT NULL,
                    Format TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    PRIMARY KEY (CardId, Format),
                    FOREIGN KEY (CardId) REFERENCES Cards(Id) ON DELETE CASCADE
                );";
            await create.ExecuteNonQueryAsync(ct);
        }

        await using (var index = conn.CreateCommand())
        {
            index.CommandText =
                "CREATE INDEX IF NOT EXISTS IX_CardLegalities_FormatStatus " +
                "ON CardLegalities(Format, Status);";
            await index.ExecuteNonQueryAsync(ct);
        }

        // Backfill is a one-shot bootstrap: only run when CardLegalities is empty
        // AND there are cards in the database. Avoids overwriting freshly-imported
        // rows (e.g. when a card's legalities flipped since last patch run).
        await using var legalityCheck = conn.CreateCommand();
        legalityCheck.CommandText = "SELECT EXISTS(SELECT 1 FROM CardLegalities)";
        var anyLegalities = Convert.ToInt32(await legalityCheck.ExecuteScalarAsync(ct)) > 0;
        if (anyLegalities) return;

        await using var cardCheck = conn.CreateCommand();
        cardCheck.CommandText = "SELECT EXISTS(SELECT 1 FROM Cards)";
        var anyCards = Convert.ToInt32(await cardCheck.ExecuteScalarAsync(ct)) > 0;
        if (!anyCards) return;

        // Older / minimal Cards tables (e.g. those used by some tests) may not
        // carry a Legalities column. Nothing to backfill in that case.
        if (!await ColumnExistsAsync(conn, table: "Cards", column: "Legalities", ct)) return;

        await BackfillCardLegalitiesAsync(conn, ct);
    }

    /// <summary>
    /// Reads each card's <c>Legalities</c> JSON blob and inserts the
    /// corresponding normalized rows in batched parameterized INSERTs. Skips
    /// empty / malformed JSON silently — the source-of-truth blob remains on
    /// <c>Cards</c>.
    /// </summary>
    private static async Task BackfillCardLegalitiesAsync(DbConnection conn, CancellationToken ct)
    {
        var rows = new List<(int CardId, string Format, string Status)>();

        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT Id, Legalities FROM Cards";
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt32(0);
                var json = reader.IsDBNull(1) ? "{}" : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(json) || json == "{}") continue;

                Dictionary<string, string>? parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (parsed is null) continue;

                foreach (var kv in parsed)
                {
                    if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                    rows.Add((id, kv.Key, kv.Value));
                }
            }
        }

        if (rows.Count == 0) return;

        await using var tx = await conn.BeginTransactionAsync(ct);
        const int chunkSize = 500;
        for (var i = 0; i < rows.Count; i += chunkSize)
        {
            var sliceEnd = Math.Min(i + chunkSize, rows.Count);
            await using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            var sb = new System.Text.StringBuilder(
                "INSERT OR IGNORE INTO CardLegalities (CardId, Format, Status) VALUES ");
            for (var j = i; j < sliceEnd; j++)
            {
                if (j > i) sb.Append(',');
                var n = j - i;
                sb.Append('(').Append("$c").Append(n)
                  .Append(", $f").Append(n)
                  .Append(", $s").Append(n).Append(')');

                var pc = insert.CreateParameter();
                pc.ParameterName = $"$c{n}";
                pc.Value = rows[j].CardId;
                insert.Parameters.Add(pc);

                var pf = insert.CreateParameter();
                pf.ParameterName = $"$f{n}";
                pf.Value = rows[j].Format;
                insert.Parameters.Add(pf);

                var ps = insert.CreateParameter();
                ps.ParameterName = $"$s{n}";
                ps.Value = rows[j].Status;
                insert.Parameters.Add(ps);
            }
            insert.CommandText = sb.ToString();
            await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
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
