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
}
