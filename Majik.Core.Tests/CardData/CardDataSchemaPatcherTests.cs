using FluentAssertions;
using Majik.Core.CardData.Database;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class CardDataSchemaPatcherTests
{
    private static SqliteConnection NewConnection()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE Cards (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);" +
                "INSERT INTO Cards (Name) VALUES ('Lightning Bolt'), ('Grizzly Bears');";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    [Fact]
    public async Task PatchAsync_AddsColumn_OnFirstRun()
    {
        await using var conn = NewConnection();

        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name='IsImplemented'";
        var count = Convert.ToInt32(await check.ExecuteScalarAsync());
        count.Should().Be(1);
    }

    [Fact]
    public async Task PatchAsync_IsIdempotent()
    {
        await using var conn = NewConnection();

        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);
        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name='IsImplemented'";
        var count = Convert.ToInt32(await check.ExecuteScalarAsync());
        count.Should().Be(1);
    }

    [Fact]
    public async Task PatchAsync_ExistingRowsDefaultToZero()
    {
        await using var conn = NewConnection();

        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT IsImplemented FROM Cards WHERE Name='Lightning Bolt'";
        var value = Convert.ToInt32(await check.ExecuteScalarAsync());
        value.Should().Be(0);
    }

    [Fact]
    public async Task PatchAsync_CreatesCompiledSpellTemplatesTable_OnFirstRun()
    {
        await using var conn = NewConnection();

        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name='CompiledSpellTemplates'";
        var count = Convert.ToInt32(await check.ExecuteScalarAsync());
        count.Should().Be(1);
    }

    [Fact]
    public async Task PatchAsync_CompiledSpellTemplatesIndex_IsCreated()
    {
        await using var conn = NewConnection();

        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='index' AND name='IX_CompiledSpellTemplates_TemplateName'";
        var count = Convert.ToInt32(await check.ExecuteScalarAsync());
        count.Should().Be(1);
    }

    [Fact]
    public async Task PatchAsync_CompiledSpellTemplatesIsIdempotent()
    {
        await using var conn = NewConnection();

        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);
        // Pre-seed a row so we can confirm the second patch doesn't recreate
        // the table (which would drop data).
        await using (var seed = conn.CreateCommand())
        {
            seed.CommandText =
                "INSERT INTO CompiledSpellTemplates (CardName, TemplateName, Priority, ParamsJson, CompiledAt) " +
                "VALUES ('Bolt', 'DamageAnyTarget', 50, '{\"n\":\"3\"}', 1700000000)";
            await seed.ExecuteNonQueryAsync();
        }

        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM CompiledSpellTemplates WHERE CardName='Bolt'";
        var count = Convert.ToInt32(await check.ExecuteScalarAsync());
        count.Should().Be(1, "second patch run must not drop or recreate the table");
    }

    private static SqliteConnection NewConnectionWithLegalities()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE Cards (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Legalities TEXT NOT NULL DEFAULT '{}');" +
                "INSERT INTO Cards (Name, Legalities) VALUES " +
                "  ('Lightning Bolt', '{\"modern\":\"legal\",\"standard\":\"not_legal\",\"vintage\":\"legal\"}')," +
                "  ('Bridge from Below', '{\"modern\":\"banned\",\"legacy\":\"legal\"}')," +
                "  ('No Legalities', '{}');";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    [Fact]
    public async Task PatchAsync_CreatesCardLegalitiesTable()
    {
        await using var conn = NewConnection();
        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CardLegalities'";
        Convert.ToInt32(await check.ExecuteScalarAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PatchAsync_CardLegalitiesIndex_IsCreated()
    {
        await using var conn = NewConnection();
        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var check = conn.CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_CardLegalities_FormatStatus'";
        Convert.ToInt32(await check.ExecuteScalarAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PatchAsync_BackfillsCardLegalities_FromLegalitiesJson()
    {
        await using var conn = NewConnectionWithLegalities();
        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var modernLegal = conn.CreateCommand();
        modernLegal.CommandText =
            "SELECT COUNT(*) FROM CardLegalities WHERE Format='modern' AND Status='legal'";
        Convert.ToInt32(await modernLegal.ExecuteScalarAsync()).Should().Be(1);

        await using var modernBanned = conn.CreateCommand();
        modernBanned.CommandText =
            "SELECT COUNT(*) FROM CardLegalities WHERE Format='modern' AND Status='banned'";
        Convert.ToInt32(await modernBanned.ExecuteScalarAsync()).Should().Be(1);

        await using var total = conn.CreateCommand();
        total.CommandText = "SELECT COUNT(*) FROM CardLegalities";
        // Bolt: modern, standard, vintage (3) + Bridge: modern, legacy (2) + empty: 0 = 5
        Convert.ToInt32(await total.ExecuteScalarAsync()).Should().Be(5);
    }

    [Fact]
    public async Task PatchAsync_CardLegalitiesBackfill_IsIdempotent()
    {
        await using var conn = NewConnectionWithLegalities();
        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);
        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var total = conn.CreateCommand();
        total.CommandText = "SELECT COUNT(*) FROM CardLegalities";
        Convert.ToInt32(await total.ExecuteScalarAsync()).Should().Be(5, "second run must not re-insert rows");
    }

    [Fact]
    public async Task PatchAsync_SkipsCardLegalitiesBackfill_WhenLegalitiesColumnAbsent()
    {
        await using var conn = NewConnection(); // no Legalities column

        // Should not throw despite the Cards table lacking a Legalities column.
        await CardDataSchemaPatcher.PatchAsync(conn, CancellationToken.None);

        await using var total = conn.CreateCommand();
        total.CommandText = "SELECT COUNT(*) FROM CardLegalities";
        Convert.ToInt32(await total.ExecuteScalarAsync()).Should().Be(0);
    }
}
