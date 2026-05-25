using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Coverage;
using Majik.Core.CardData.Database;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Coverage;

/// <summary>
/// Aggregation tests for <see cref="CoverageReportV2"/>. Builds a tiny
/// synthetic pool covering every tier and asserts tier counts, weighted
/// rollups, and decklist-weighted percentages.
/// </summary>
public class CoverageReportV2Tests
{
    private static CoverageClassifier BuildClassifier(
        IEnumerable<CardEntity> entities,
        IEnumerable<string>? namedFactoryNames = null)
    {
        var repo = new FakeRepo(entities);
        var factory = new ScryfallCardFactory(repo);
        var stub = new Player("Synth", 20);
        var names = new HashSet<string>(
            namedFactoryNames ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);
        return new CoverageClassifier(factory, stub, names);
    }

    private static readonly CardEntity Bear = new()
    {
        Name = "Plain Bear",
        TypeLine = "Creature — Bear",
        ManaCost = "{1}{G}",
        Power = "2",
        Toughness = "2",
        OracleText = "",
    };

    private static readonly CardEntity FlyingDrake = new()
    {
        Name = "Test Drake",
        TypeLine = "Creature — Drake",
        ManaCost = "{2}{U}",
        Power = "2",
        Toughness = "2",
        OracleText = "Flying",
        Keywords = "[\"Flying\"]",
    };

    private static readonly CardEntity FakeBolt = new()
    {
        Name = "Fake Bolt",
        TypeLine = "Instant",
        ManaCost = "{R}",
        OracleText = "Fake Bolt deals 3 damage to any target.",
    };

    private static readonly CardEntity Unimplemented = new()
    {
        Name = "Fictional Wizard",
        TypeLine = "Creature — Human Wizard",
        ManaCost = "{2}{U}",
        Power = "1",
        Toughness = "3",
        OracleText = "Whenever you cast a quoogle, scry 7 and gain the moon.",
        Keywords = "[]",
    };

    [Fact]
    public void Build_TierCounts_AreCorrect()
    {
        var entities = new[] { Bear, FlyingDrake, FakeBolt, Unimplemented };
        var cls = BuildClassifier(entities);

        var report = CoverageReportV2.Build("test", entities, cls);

        report.TotalCards.Should().Be(4);
        report.CountsByTier[CoverageTier.Vanilla].Should().Be(1);
        report.CountsByTier[CoverageTier.KeywordOnly].Should().Be(1);
        report.CountsByTier[CoverageTier.SpellBound].Should().Be(1);
        report.CountsByTier[CoverageTier.Unimplemented].Should().Be(1);
        report.CountsByTier[CoverageTier.NamedFactory].Should().Be(0);
        report.CoveredCards.Should().Be(3);
        report.CoveredPercent.Should().BeApproximately(75.0, 0.01);
    }

    [Fact]
    public void Build_Decklist_Weights_Apply_To_WeightedRollups()
    {
        var entities = new[] { FakeBolt, Unimplemented };
        var cls = BuildClassifier(entities);

        var weights = new Dictionary<string, int>
        {
            ["Fake Bolt"] = 4,
            ["Fictional Wizard"] = 1,
        };

        var report = CoverageReportV2.Build("deck", entities, cls, weights);

        report.TotalCards.Should().Be(2); // distinct names
        report.TotalWeight.Should().Be(5);
        report.WeightedByTier[CoverageTier.SpellBound].Should().Be(4);
        report.WeightedByTier[CoverageTier.Unimplemented].Should().Be(1);
        report.WeightedCovered.Should().Be(4);
        report.WeightedCoveredPercent.Should().BeApproximately(80.0, 0.01);
    }

    [Fact]
    public void Build_TopUnimplemented_Sorted_ByWeightDescending()
    {
        var u2 = new CardEntity
        {
            Name = "Other Mystery",
            TypeLine = "Sorcery",
            ManaCost = "{2}{B}",
            OracleText = "Cast a quoogle.",
        };
        var entities = new[] { Unimplemented, u2 };
        var cls = BuildClassifier(entities);

        var weights = new Dictionary<string, int>
        {
            ["Fictional Wizard"] = 1,
            ["Other Mystery"] = 4,
        };
        var report = CoverageReportV2.Build("deck", entities, cls, weights, topUnimplemented: 5);

        report.TopUnimplemented[0].Name.Should().Be("Other Mystery");
        report.TopUnimplemented[0].Weight.Should().Be(4);
    }

    [Fact]
    public void NamedFactory_Tier_Has_Priority_Over_SpellBound()
    {
        // A bolt-shaped instant that ALSO is in the named-factory set —
        // must classify as NamedFactory regardless of the template match.
        var cls = BuildClassifier(new[] { FakeBolt }, namedFactoryNames: new[] { "Fake Bolt" });
        cls.Classify(FakeBolt).Should().Be(CoverageTier.NamedFactory);
    }

    [Fact]
    public void FrontFace_StripsDoubleSlashSuffix_OrReturnsInput()
    {
        // Scryfall DFC / adventure / split / MDFC naming convention.
        CoverageReportV2.FrontFace("Sink into Stupor // Soporific Springs")
            .Should().Be("Sink into Stupor");
        CoverageReportV2.FrontFace("Grief // Grief").Should().Be("Grief");
        CoverageReportV2.FrontFace("Lightning Bolt").Should().Be("Lightning Bolt");
        CoverageReportV2.FrontFace("").Should().Be("");
    }

    [Fact]
    public void Frequency_DfcEntity_MatchesSnapshot_ByFrontFace()
    {
        // Snapshot lists the card by its printed front-face name only.
        // The DB row carries the full "Front // Back" string. The report
        // must still credit the snapshot weight to the front-face entity.
        var dfc = new CardEntity
        {
            Name = "Sink into Stupor // Soporific Springs",
            TypeLine = "Instant // Land",
            ManaCost = "{1}{U}",
            OracleText = "Tap target creature. It doesn't untap during its controller's next untap step.",
        };
        var entities = new[] { dfc };
        var cls = BuildClassifier(entities);

        var freq = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Sink into Stupor"] = 340.0,
        };

        var report = CoverageReportV2.Build("test", entities, cls,
            frequencyWeights: freq);

        report.FrequencyTotalWeight.Should().Be(340.0);
        report.NotInSet.Should().NotBeNull().And.BeEmpty();
        report.NotInSetWeight.Should().Be(0.0);
        // The TopMeta row is keyed by the snapshot name (what the user knows).
        report.TopMeta!.Should().ContainSingle(r => r.Name == "Sink into Stupor");
    }

    [Fact]
    public void Frequency_ExactNameWins_OverDfcMirror_NoDoubleCount()
    {
        // Snapshot lists "Lightning Bolt". The DB has both a real
        // "Lightning Bolt" row and a stray "Lightning Bolt // Lightning Bolt"
        // mirror row (some printings collapse to this). The snapshot
        // weight must be counted exactly once, against the exact-name row.
        var exact = new CardEntity
        {
            Name = "Lightning Bolt",
            TypeLine = "Instant",
            ManaCost = "{R}",
            OracleText = "Lightning Bolt deals 3 damage to any target.",
        };
        var mirror = new CardEntity
        {
            Name = "Lightning Bolt // Lightning Bolt",
            TypeLine = "Instant",
            ManaCost = "{R}",
            OracleText = "Whatever weird thing.",
        };
        var entities = new[] { mirror, exact }; // mirror iterates first.
        var cls = BuildClassifier(entities);

        var freq = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Lightning Bolt"] = 300.0,
        };

        var report = CoverageReportV2.Build("test", entities, cls,
            frequencyWeights: freq);

        report.FrequencyTotalWeight.Should().Be(300.0); // not 600.
        report.NotInSetWeight.Should().Be(0.0);
    }

    [Fact]
    public void Frequency_UnmatchedSnapshotCard_LandsInNotInSet()
    {
        // Snapshot has a card the entity pool does not — surface it via
        // NotInSet so callers can see what's missing.
        var entities = new[] { FakeBolt };
        var cls = BuildClassifier(entities);

        var freq = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Fake Bolt"] = 100.0,
            ["Missing Card"] = 50.0,
        };

        var report = CoverageReportV2.Build("test", entities, cls,
            frequencyWeights: freq);

        report.NotInSet.Should().NotBeNull();
        report.NotInSet!.Should().ContainSingle();
        report.NotInSet![0].Name.Should().Be("Missing Card");
        report.NotInSet![0].Weight.Should().Be(50.0);
        report.NotInSetWeight.Should().Be(50.0);
        report.FrequencyTotalWeight.Should().Be(100.0);
    }

    [Fact]
    public void Frequency_Idempotent_RunsTwice_SameNotInSet()
    {
        var entities = new[] { FakeBolt };
        var cls = BuildClassifier(entities);
        var freq = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Fake Bolt"] = 100.0,
            ["Missing A"] = 50.0,
            ["Missing B"] = 25.0,
        };

        var first = CoverageReportV2.Build("test", entities, cls, frequencyWeights: freq);
        var second = CoverageReportV2.Build("test", entities, cls, frequencyWeights: freq);

        first.NotInSet!.Count.Should().Be(second.NotInSet!.Count);
        first.NotInSetWeight.Should().Be(second.NotInSetWeight);
        first.FrequencyTotalWeight.Should().Be(second.FrequencyTotalWeight);
        first.NotInSet.Select(r => r.Name).Should()
            .Equal(second.NotInSet.Select(r => r.Name));
    }

    [Fact]
    public void Frequency_CaseInsensitive_FallbackMatch()
    {
        var entity = new CardEntity
        {
            Name = "the one ring", // mismatched casing vs snapshot
            TypeLine = "Legendary Artifact",
            ManaCost = "{4}",
            OracleText = "Indestructible.",
            Keywords = "[\"Indestructible\"]",
        };
        var entities = new[] { entity };
        var cls = BuildClassifier(entities);

        var freq = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["The One Ring"] = 220.0,
        };

        var report = CoverageReportV2.Build("test", entities, cls,
            frequencyWeights: freq);

        report.FrequencyTotalWeight.Should().Be(220.0);
        report.NotInSetWeight.Should().Be(0.0);
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
