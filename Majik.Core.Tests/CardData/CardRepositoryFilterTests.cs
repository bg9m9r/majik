using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for the color / type / CMC filter parameters added to
/// <see cref="ICardRepository.Search"/> (Task 2 of the Deck Polish plan).
/// Uses an in-memory SQLite connection so the production cards.db is never touched.
/// </summary>
public class CardRepositoryFilterTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly CardDbContext _db;
    private readonly DbCardRepository _repo;

    public CardRepositoryFilterTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<CardDbContext>().UseSqlite(_conn).Options;
        _db = new CardDbContext(opts);
        _db.Database.EnsureCreated();
        _repo = new DbCardRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    // ---------------------------------------------------------------------------
    // Helper builders
    // ---------------------------------------------------------------------------

    private static CardEntity Card(
        string name,
        string typeLine = "Creature — Human",
        int? cmc = 1,
        string colors = "[]",
        bool implemented = true) =>
        new()
        {
            ScryfallId = Guid.NewGuid().ToString(),
            Name = name,
            TypeLine = typeLine,
            Cmc = cmc,
            Colors = colors,
            IsImplemented = implemented,
            ManaCost = "",
            Set = "TST",
            CollectorNumber = "1",
            ImportedAt = DateTime.UtcNow,
        };

    private void Seed(params CardEntity[] cards)
    {
        _db.Cards.AddRange(cards);
        _db.SaveChanges();
    }

    // ---------------------------------------------------------------------------
    // Color filter tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Search_filters_by_colors_single_color()
    {
        Seed(
            Card("Lightning Bolt", typeLine: "Instant", cmc: 1, colors: "[\"R\"]"),
            Card("Counterspell", typeLine: "Instant", cmc: 2, colors: "[\"U\"]"),
            Card("Giant Growth", typeLine: "Instant", cmc: 1, colors: "[\"G\"]"));

        var results = _repo.Search(null, false, 50, colors: new[] { "R" });

        results.Should().ContainSingle(c => c.Name == "Lightning Bolt");
    }

    [Fact]
    public void Search_C_filter_matches_colorless_cards()
    {
        Seed(
            Card("Mox Diamond", typeLine: "Artifact", cmc: 0, colors: "[]"),
            Card("Wastes", typeLine: "Basic Land", cmc: null, colors: "[]"),
            Card("Lightning Bolt", typeLine: "Instant", cmc: 1, colors: "[\"R\"]"));

        var results = _repo.Search(null, false, 50, colors: new[] { "C" });

        results.Select(c => c.Name)
               .Should().BeEquivalentTo(new[] { "Mox Diamond", "Wastes" });
    }

    [Fact]
    public void Search_multi_color_filter_matches_any_included_color()
    {
        Seed(
            Card("Bolt", typeLine: "Instant", cmc: 1, colors: "[\"R\"]"),
            Card("Counterspell", typeLine: "Instant", cmc: 2, colors: "[\"U\"]"),
            Card("Giant Growth", typeLine: "Instant", cmc: 1, colors: "[\"G\"]"));

        // R or U — should exclude Green
        var results = _repo.Search(null, false, 50, colors: new[] { "R", "U" });

        results.Select(c => c.Name)
               .Should().BeEquivalentTo(new[] { "Bolt", "Counterspell" });
    }

    // ---------------------------------------------------------------------------
    // Type filter tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Search_filters_by_type_line_supertype()
    {
        Seed(
            Card("Lightning Bolt", typeLine: "Instant", cmc: 1),
            Card("Grizzly Bears", typeLine: "Creature — Bear", cmc: 2),
            Card("Dark Ritual", typeLine: "Instant", cmc: 1, colors: "[\"B\"]"));

        var results = _repo.Search(null, false, 50, types: new[] { "Instant" });

        results.Select(c => c.Name)
               .Should().BeEquivalentTo(new[] { "Lightning Bolt", "Dark Ritual" });
    }

    [Fact]
    public void Search_type_filter_is_case_insensitive()
    {
        Seed(
            Card("Bolt", typeLine: "Instant"),
            Card("Bears", typeLine: "Creature — Bear"));

        var results = _repo.Search(null, false, 50, types: new[] { "instant" });

        results.Should().ContainSingle(c => c.Name == "Bolt");
    }

    [Fact]
    public void Search_type_filter_stops_at_em_dash_separator()
    {
        // "Bear" is a subtype; should NOT match a filter for "Bear" as supertype
        // when using the left-of-em-dash tokens.
        Seed(
            Card("Grizzly Bears", typeLine: "Creature — Bear", cmc: 2),
            Card("Bear Cub", typeLine: "Creature — Bear", cmc: 1),
            Card("Forest Bear", typeLine: "Creature — Bear", cmc: 2));

        // Filter on "Creature" (supertype) should return all three; "Bear" alone should not.
        var byCreature = _repo.Search(null, false, 50, types: new[] { "Creature" });
        byCreature.Should().HaveCount(3);

        var byBear = _repo.Search(null, false, 50, types: new[] { "Bear" });
        byBear.Should().BeEmpty("Bear is a subtype after the em-dash");
    }

    // ---------------------------------------------------------------------------
    // CMC bucket tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Search_filters_by_exact_cmc_bucket()
    {
        Seed(
            Card("Opt", typeLine: "Instant", cmc: 1),
            Card("Counterspell", typeLine: "Instant", cmc: 2),
            Card("Wrath of God", typeLine: "Sorcery", cmc: 4));

        var results = _repo.Search(null, false, 50, cmcBuckets: new[] { 2 });

        results.Should().ContainSingle(c => c.Name == "Counterspell");
    }

    [Fact]
    public void Search_cmc_bucket_7_matches_7_and_above()
    {
        Seed(
            Card("Small", typeLine: "Creature", cmc: 2),
            Card("BigSeven", typeLine: "Creature", cmc: 7),
            Card("BigTen", typeLine: "Creature", cmc: 10),
            Card("Biggest", typeLine: "Creature", cmc: 15));

        var results = _repo.Search(null, false, 50, cmcBuckets: new[] { 7 });

        results.Should().HaveCount(3);
        results.Should().NotContain(c => c.Name == "Small");
    }

    [Fact]
    public void Search_cards_with_null_cmc_excluded_from_cmc_filter()
    {
        Seed(
            Card("Land", typeLine: "Basic Land", cmc: null),
            Card("Opt", typeLine: "Instant", cmc: 1));

        var results = _repo.Search(null, false, 50, cmcBuckets: new[] { 0, 1, 2 });

        // Land has null Cmc and must be excluded (CR 202.3: lands have no mana cost/CMC).
        results.Should().ContainSingle(c => c.Name == "Opt");
    }

    // ---------------------------------------------------------------------------
    // AND-across-dimensions test
    // ---------------------------------------------------------------------------

    [Fact]
    public void Search_AND_semantics_across_color_type_and_cmc()
    {
        Seed(
            Card("Lightning Bolt", typeLine: "Instant", cmc: 1, colors: "[\"R\"]"),
            Card("Force of Will", typeLine: "Instant", cmc: 5, colors: "[\"U\"]"),
            Card("Grizzly Bears", typeLine: "Creature — Bear", cmc: 2, colors: "[\"G\"]"),
            Card("Shock", typeLine: "Instant", cmc: 1, colors: "[\"R\"]"));

        var results = _repo.Search(
            null, false, 50,
            colors: new[] { "R" },
            types: new[] { "Instant" },
            cmcBuckets: new[] { 1 });

        // Both Bolt and Shock are R / Instant / CMC 1; Force of Will and Bears are excluded.
        results.Select(c => c.Name)
               .Should().BeEquivalentTo(new[] { "Lightning Bolt", "Shock" });
    }

    // ---------------------------------------------------------------------------
    // Null / empty filter = no filtering
    // ---------------------------------------------------------------------------

    [Fact]
    public void Search_null_filters_return_all_rows()
    {
        Seed(
            Card("Alpha"),
            Card("Beta"),
            Card("Gamma"));

        var results = _repo.Search(null, false, 50, colors: null, types: null, cmcBuckets: null);

        results.Should().HaveCount(3);
    }
}
