using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Coverage;
using Majik.Core.CardData.Database;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Coverage;

/// <summary>
/// Tests for <see cref="TournamentFrequencySource"/> + the
/// tournament-frequency-weighted code paths through
/// <see cref="CoverageReportV2"/>. Snapshots are inlined so the suite is
/// hermetic — no I/O.
/// </summary>
public class TournamentFrequencyTests
{
    private const string FixtureSnapshot = """
    {
      "format": "modern",
      "snapshot_date": "2026-05-24",
      "cards": [
        { "name": "Lightning Bolt", "decks": 1000, "play_rate_pct": 30.0 },
        { "name": "Counterspell",   "decks": 800,  "play_rate_pct": 20.0 },
        { "name": "Fatal Push",     "decks": 600,  "play_rate_pct": 15.0 },
        { "name": "Path to Exile",  "decks": 500,  "play_rate_pct": 12.0 },
        { "name": "Mox Opal",       "decks": 100,  "play_rate_pct": 5.0  }
      ]
    }
    """;

    [Fact]
    public void ParseSnapshot_LoadsAllRows_AsPlayRatePctTimesTen()
    {
        var map = TournamentFrequencySource.ParseSnapshot(FixtureSnapshot);

        map.Should().HaveCount(5);
        // play_rate_pct is scaled ×10 to keep weights in a stable numeric scale.
        map["Lightning Bolt"].Should().BeApproximately(300.0, 0.001);
        map["Counterspell"].Should().BeApproximately(200.0, 0.001);
        map["Mox Opal"].Should().BeApproximately(50.0, 0.001);
    }

    [Fact]
    public void ParseSnapshot_FallsBackToDecks_WhenPlayRateAbsent()
    {
        const string json = """
        { "format": "modern", "cards": [
            { "name": "OnlyDecks", "decks": 250 },
            { "name": "OnlyName" }
          ] }
        """;

        var map = TournamentFrequencySource.ParseSnapshot(json);

        map.Should().ContainKey("OnlyDecks").WhoseValue.Should().Be(250.0);
        // No play_rate, no decks → falls back to weight 1.
        map.Should().ContainKey("OnlyName").WhoseValue.Should().Be(1.0);
    }

    [Fact]
    public void ParseSnapshot_RejectsMalformedJson()
    {
        Action act = () => TournamentFrequencySource.ParseSnapshot("{ not-json");
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ParseSnapshot_RejectsMissingCardsArray()
    {
        Action act = () => TournamentFrequencySource.ParseSnapshot("{ \"format\": \"modern\" }");
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void LoadFromSnapshot_RoundTrips_ViaTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meta-snap-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, FixtureSnapshot);
            var map = TournamentFrequencySource.LoadFromSnapshot(path);
            map["Lightning Bolt"].Should().BeApproximately(300.0, 0.001);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromSnapshot_Throws_WhenFileMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.json");
        Action act = () => TournamentFrequencySource.LoadFromSnapshot(path);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Build_FrequencyWeightedCoverage_ComputesCorrectly()
    {
        // Spec example: 2 covered cards weight 100 each + 1 uncovered weight 50 = 80% weighted.
        var bolt = new CardEntity
        {
            Name = "Fake Bolt",
            TypeLine = "Instant",
            ManaCost = "{R}",
            OracleText = "Fake Bolt deals 3 damage to any target.",
        };
        var bear = new CardEntity
        {
            Name = "Plain Bear",
            TypeLine = "Creature — Bear",
            ManaCost = "{1}{G}",
            Power = "2",
            Toughness = "2",
            OracleText = "",
        };
        var unimpl = new CardEntity
        {
            Name = "Fictional Wizard",
            TypeLine = "Creature — Human Wizard",
            ManaCost = "{2}{U}",
            Power = "1",
            Toughness = "3",
            OracleText = "Whenever you cast a quoogle, scry 7.",
            Keywords = "[]",
        };

        var entities = new[] { bolt, bear, unimpl };
        var classifier = BuildClassifier(entities);

        var freq = new Dictionary<string, double>
        {
            ["Fake Bolt"] = 100,         // SpellBound (covered)
            ["Plain Bear"] = 100,        // Vanilla (covered)
            ["Fictional Wizard"] = 50,   // Unimplemented
        };

        var report = CoverageReportV2.Build(
            scope: "test",
            entities: entities,
            classifier: classifier,
            frequencyWeights: freq);

        report.FrequencyTotalWeight.Should().Be(250.0);
        report.FrequencyWeightedCovered.Should().Be(200.0);
        report.FrequencyWeightedCoveredPercent.Should().BeApproximately(80.0, 0.001);
    }

    [Fact]
    public void Build_FrequencyWeights_MissingCards_AreSkipped_NotErrored()
    {
        var bolt = new CardEntity
        {
            Name = "Fake Bolt",
            TypeLine = "Instant",
            ManaCost = "{R}",
            OracleText = "Fake Bolt deals 3 damage to any target.",
        };
        var entities = new[] { bolt };
        var classifier = BuildClassifier(entities);

        // "Ghost Card" is not in the entity pool; the report should ignore it
        // (no error) and "Fake Bolt" missing from freq should also not error.
        var freq = new Dictionary<string, double>
        {
            ["Ghost Card"] = 999,
        };

        var report = CoverageReportV2.Build("test", entities, classifier, frequencyWeights: freq);

        report.FrequencyTotalWeight.Should().Be(0.0);
        report.FrequencyWeightedCoveredPercent.Should().Be(0.0);
        report.FrequencyWeightedByTier.Should().NotBeNull();
    }

    [Fact]
    public void Build_TopMeta_RanksByFrequencyWeightDescending()
    {
        var bolt = new CardEntity
        {
            Name = "Fake Bolt",
            TypeLine = "Instant",
            ManaCost = "{R}",
            OracleText = "Fake Bolt deals 3 damage to any target.",
        };
        var bear = new CardEntity
        {
            Name = "Plain Bear",
            TypeLine = "Creature — Bear",
            ManaCost = "{1}{G}",
            Power = "2",
            Toughness = "2",
            OracleText = "",
        };
        var entities = new[] { bolt, bear };
        var classifier = BuildClassifier(entities);
        var freq = new Dictionary<string, double>
        {
            ["Fake Bolt"] = 300,
            ["Plain Bear"] = 100,
        };

        var report = CoverageReportV2.Build("test", entities, classifier, frequencyWeights: freq);

        report.TopMeta.Should().NotBeNull();
        report.TopMeta![0].Name.Should().Be("Fake Bolt");
        report.TopMeta[0].Weight.Should().Be(300);
        report.TopMeta[1].Name.Should().Be("Plain Bear");
        report.TopMetaCovered.Should().Be(2);
        report.TopMetaTotal.Should().Be(2);
    }

    [Fact]
    public void Build_WithoutFrequencyWeights_LeavesFreqFieldsNull()
    {
        var bear = new CardEntity
        {
            Name = "Plain Bear",
            TypeLine = "Creature — Bear",
            ManaCost = "{1}{G}",
            Power = "2",
            Toughness = "2",
            OracleText = "",
        };
        var entities = new[] { bear };
        var classifier = BuildClassifier(entities);

        var report = CoverageReportV2.Build("test", entities, classifier);

        report.FrequencyWeightedByTier.Should().BeNull();
        report.TopMeta.Should().BeNull();
        report.FrequencyTotalWeight.Should().Be(0.0);
    }

    private static CoverageClassifier BuildClassifier(IEnumerable<CardEntity> entities)
    {
        var repo = new FakeRepo(entities);
        var factory = new ScryfallCardFactory(repo);
        var stub = new Player("Synth", 20);
        return new CoverageClassifier(
            factory, stub, new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class FakeRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _by;
        public FakeRepo(IEnumerable<CardEntity> entities)
        {
            _by = entities.ToDictionary(e => e.Name, e => e, StringComparer.Ordinal);
        }
        public CardEntity? GetByName(string name) =>
            !string.IsNullOrWhiteSpace(name) && _by.TryGetValue(name, out var e) ? e : null;
        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(GetByName).OfType<CardEntity>().ToList();
        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) => throw new NotSupportedException();
        public bool IsImplemented(string name) => false;
        public void SetImplemented(string name, bool value) => throw new NotSupportedException();
    }
}
