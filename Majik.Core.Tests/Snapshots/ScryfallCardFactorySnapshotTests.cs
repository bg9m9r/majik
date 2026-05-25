using System.Text.Json;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Tests.Snapshots;

/// <summary>
/// Golden-snapshot regression suite for <see cref="ScryfallCardFactory"/>.
/// Each fixture card has a bundled <see cref="CardEntity"/> JSON (the
/// production-DB row, exported via <c>SNAPSHOT_RECORD=1</c>) and a snapshot
/// JSON capturing the output of <see cref="ScryfallCardFactory.Create"/>.
/// A diff in the rebuilt snapshot vs the committed one fails the test.
///
/// CI runs without the user's <c>cards.db</c>, so both halves of the fixture
/// (DB row + snapshot) are shipped in source. To extend the suite, add a
/// name to <c>snapshot-cards.json</c> then rerun with
/// <c>SNAPSHOT_RECORD=1</c> against a local DB.
/// </summary>
public class ScryfallCardFactorySnapshotTests
{
    private const string RecordEnvVar = "SNAPSHOT_RECORD";

    private readonly ITestOutputHelper _output;

    public ScryfallCardFactorySnapshotTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> FixtureCards()
    {
        var file = SnapshotPaths.FixtureFile;
        if (!File.Exists(file)) yield break;
        var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))
                   ?? new List<string>();
        foreach (var name in list)
        {
            yield return new object[] { name };
        }
    }

    [Theory]
    [MemberData(nameof(FixtureCards))]
    public void Snapshot_Matches_For(string cardName)
    {
        var slug = SnapshotPaths.Slug(cardName);
        var dataPath = Path.Combine(SnapshotPaths.CardDataDir, slug + ".json");
        var snapPath = Path.Combine(SnapshotPaths.SnapshotsDir, slug + ".json");

        if (IsRecording)
        {
            // Recording path: refresh both the bundled DB row (from the
            // local cards.db) and the snapshot it produces. Skip cards that
            // aren't in the live DB so a partial Scryfall import doesn't
            // wipe an existing fixture file.
            if (!SnapshotRecorder.IsLocalDbAvailable())
            {
                _output.WriteLine(
                    $"[record] skipped {cardName} — no local cards.db available");
                return;
            }
            var refreshed = SnapshotRecorder.RefreshCardData(cardName);
            if (!refreshed)
            {
                _output.WriteLine(
                    $"[record] skipped {cardName} — not present in local cards.db");
                return;
            }
        }

        var (card, bus) = BuildCard(cardName, dataPath);

        var summary = SnapshotSummary.Build(card, bus);
        var actual = SnapshotSummary.Serialize(summary);

        if (IsRecording)
        {
            Directory.CreateDirectory(SnapshotPaths.SnapshotsDir);
            File.WriteAllText(snapPath, actual);
            _output.WriteLine($"[record] wrote {snapPath} ({actual.Length} bytes)");
            return;
        }

        File.Exists(snapPath).Should().BeTrue(
            $"snapshot file missing for {cardName}; run with " +
            $"{RecordEnvVar}=1 against a local DB to seed it");

        var expected = File.ReadAllText(snapPath);
        if (expected != actual)
        {
            _output.WriteLine("--- expected ---");
            _output.WriteLine(expected);
            _output.WriteLine("--- actual ---");
            _output.WriteLine(actual);
        }
        actual.Should().Be(expected,
            $"snapshot for {cardName} drifted; if intentional, rerun with " +
            $"{RecordEnvVar}=1 to update it.");
    }

    [Fact]
    public void FixtureList_HasTwoHundredCards()
    {
        var file = SnapshotPaths.FixtureFile;
        File.Exists(file).Should().BeTrue(
            $"missing snapshot-cards.json at {file}");
        var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file))
                   ?? new List<string>();
        list.Should().HaveCount(200,
            "the snapshot suite is sized to 200 representative cards");
        list.Should().OnlyHaveUniqueItems("duplicates would collide on the same slug");
    }

    [Fact]
    public void Every_Fixture_Builds_NonEmpty_Snapshot()
    {
        if (IsRecording) return; // recording mode is allowed to start blank

        var fixtureList = JsonSerializer.Deserialize<List<string>>(
            File.ReadAllText(SnapshotPaths.FixtureFile)) ?? new List<string>();

        var missingDataRows = new List<string>();
        foreach (var name in fixtureList)
        {
            var slug = SnapshotPaths.Slug(name);
            var dataPath = Path.Combine(SnapshotPaths.CardDataDir, slug + ".json");
            if (!File.Exists(dataPath))
            {
                missingDataRows.Add(name);
                continue;
            }
            var (card, bus) = BuildCard(name, dataPath);
            var obj = SnapshotSummary.Build(card, bus);
            obj.Should().NotBeNull();
            ((string?)obj["name"]).Should().NotBeNullOrEmpty(
                $"snapshot for {name} must carry the card name");
        }

        missingDataRows.Should().BeEmpty(
            "every fixture name needs a card-data row; missing rows: " +
            string.Join(", ", missingDataRows));
    }

    [Fact]
    public void Snapshot_Is_Deterministic_Across_Two_Builds()
    {
        if (IsRecording) return;

        var fixtureList = JsonSerializer.Deserialize<List<string>>(
            File.ReadAllText(SnapshotPaths.FixtureFile)) ?? new List<string>();
        // One representative card is enough to prove determinism; the snapshot
        // diff in the parameterised test catches any per-card flakiness.
        // Pick "Lightning Bolt" if available, else the first entry.
        var name = fixtureList.Contains("Lightning Bolt")
            ? "Lightning Bolt"
            : fixtureList.FirstOrDefault();
        if (name is null) return;

        var dataPath = Path.Combine(SnapshotPaths.CardDataDir,
            SnapshotPaths.Slug(name) + ".json");
        if (!File.Exists(dataPath)) return;

        var (c1, b1) = BuildCard(name, dataPath);
        var (c2, b2) = BuildCard(name, dataPath);
        var s1 = SnapshotSummary.Serialize(SnapshotSummary.Build(c1, b1));
        var s2 = SnapshotSummary.Serialize(SnapshotSummary.Build(c2, b2));
        s2.Should().Be(s1,
            $"two builds of {name} must produce byte-identical snapshots");
    }

    private static bool IsRecording =>
        string.Equals(Environment.GetEnvironmentVariable(RecordEnvVar),
            "1", StringComparison.Ordinal);

    /// <summary>
    /// Build a card via <see cref="ScryfallCardFactory.Create"/> from a single
    /// bundled <see cref="CardEntity"/> JSON row. The factory is wired with a
    /// fresh <see cref="ReplacementBus"/> + <see cref="ContinuousEffectsService"/>
    /// so the binders that push replacements (ETB-tapped, ETB-with-counters,
    /// enters-as-copy) actually fire — and so the snapshot can introspect them.
    /// </summary>
    private static (Majik.Core.Cards.ICard card, ReplacementBus bus) BuildCard(
        string cardName, string dataPath)
    {
        if (!File.Exists(dataPath))
        {
            throw new FileNotFoundException(
                $"card-data row missing for {cardName} at {dataPath}; " +
                $"run with {RecordEnvVar}=1 against a local DB to seed it.");
        }

        CardEntity? entity;
        using (var stream = File.OpenRead(dataPath))
        {
            entity = JsonSerializer.Deserialize<CardEntity>(stream);
        }
        if (entity is null)
        {
            throw new InvalidDataException(
                $"card-data row for {cardName} deserialised to null.");
        }

        var repo = new FixtureCardRepository(new[] { entity });
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();
        var factory = new ScryfallCardFactory(repo, replacements: bus, effects: effects);
        var owner = new Player("Snapshot Owner", 20);
        var card = factory.Create(cardName, owner);
        return (card, bus);
    }
}
