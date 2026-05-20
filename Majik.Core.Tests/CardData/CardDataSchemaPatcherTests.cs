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
}
