using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Hits the user-installed Scryfall SQLite DB if present. Tests are no-ops
/// when the DB file is missing, so CI without the bulk import still passes.
/// </summary>
public class LiveDbIntegrationTests
{
    private static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Majik", "cards.db");

    /// <summary>
    /// True only when a real Scryfall import lives at <see cref="DbPath"/>.
    /// File-existence alone isn't enough: other tests (and the test host
    /// itself when it spins up Majik.Server / CardDbIndexBootstrapper) can
    /// touch the path and leave an empty SQLite file with no Cards table.
    /// On CI that empty file fooled the old <c>File.Exists</c> guard into
    /// running these tests, which then crashed on "no such table: Cards".
    /// </summary>
    private static bool DbAvailable()
    {
        if (!File.Exists(DbPath)) return false;
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={DbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Cards'";
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void Create_LightningBolt_FromRealDb_HasInstantType()
    {
        if (!DbAvailable()) return;

        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));
        var alice = new Player("Alice", 20);

        var card = factory.Create("Lightning Bolt", alice);

        card.Should().BeOfType<Instant>();
        card.ManaCost.Should().Be("R");
    }

    [Fact]
    public void Create_Mountain_FromRealDb_HasBasicSupertypeAndManaAbility()
    {
        if (!DbAvailable()) return;

        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));
        var alice = new Player("Alice", 20);

        var card = factory.Create("Mountain", alice);

        card.Should().BeOfType<Land>();
        card.HasSupertype(CardSupertype.Basic).Should().BeTrue();
        card.Abilities.OfType<IManaAbility>().Should().ContainSingle();
    }

    [Fact]
    public void Create_GrizzlyBears_FromRealDb_2_2()
    {
        if (!DbAvailable()) return;

        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));
        var alice = new Player("Alice", 20);

        var card = (Creature)factory.Create("Grizzly Bears", alice);

        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
    }
}
