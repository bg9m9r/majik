using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Majik.Console.Commands;
using Majik.Core.CardData;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit coverage for <see cref="ExportModernCardsCommand"/> — the filter,
/// dedupe, implemented-flag and round-trip behaviour of the seed-export
/// CLI. Uses a small synthetic Scryfall payload (5 cards exercising
/// banned / not_legal / legal-implemented / legal-unimplemented and a
/// multi-printing dedupe pair) so the test stays fast and doesn't need
/// the real ~500 MB Scryfall bulk file on disk.
/// </summary>
public class ExportModernCardsCommandTests
{
    /// <summary>Sanity check overriding the default Forest / Mountain
    /// asserts — used by tests whose synthetic input only carries
    /// Lightning Bolt. The verification logic itself is exercised by
    /// the dedicated round-trip test below.</summary>
    private static readonly IReadOnlyList<(string Name, string MustContain)>
        BoltOnlySanityCheck = new (string, string)[]
        {
            ("Lightning Bolt", "3 damage"),
        };

    private const string SyntheticScryfallJson = """
        [
          {
            "id": "id-bolt",
            "name": "Lightning Bolt",
            "mana_cost": "{R}",
            "type_line": "Instant",
            "oracle_text": "Lightning Bolt deals 3 damage to any target.",
            "colors": ["R"],
            "color_identity": ["R"],
            "cmc": 1.0,
            "legalities": { "modern": "legal" },
            "released_at": "2021-06-18"
          },
          {
            "id": "id-bolt-older",
            "name": "Lightning Bolt",
            "mana_cost": "{R}",
            "type_line": "Instant",
            "oracle_text": "Lightning Bolt deals 3 damage to any target. (old wording)",
            "colors": ["R"],
            "color_identity": ["R"],
            "cmc": 1.0,
            "legalities": { "modern": "legal" },
            "released_at": "2010-07-16"
          },
          {
            "id": "id-path",
            "name": "Path to Exile",
            "mana_cost": "{W}",
            "type_line": "Instant",
            "oracle_text": "Exile target creature. Its controller may search...",
            "colors": ["W"],
            "color_identity": ["W"],
            "cmc": 1.0,
            "legalities": { "modern": "legal" },
            "released_at": "2019-08-23"
          },
          {
            "id": "id-recall",
            "name": "Ancestral Recall",
            "mana_cost": "{U}",
            "type_line": "Instant",
            "oracle_text": "Target player draws three cards.",
            "colors": ["U"],
            "color_identity": ["U"],
            "cmc": 1.0,
            "legalities": { "modern": "not_legal" },
            "released_at": "1993-08-05"
          },
          {
            "id": "id-bridge",
            "name": "Bridge from Below",
            "mana_cost": "{B}{B}",
            "type_line": "Enchantment",
            "oracle_text": "...",
            "colors": ["B"],
            "color_identity": ["B"],
            "cmc": 2.0,
            "legalities": { "modern": "banned" },
            "released_at": "2003-05-23"
          }
        ]
        """;

    [Fact]
    public async Task Run_FiltersBannedAndNotLegal_KeepsLegalOnly()
    {
        using var tmp = new TempFiles();
        var inputPath = tmp.WriteText("scryfall.json", SyntheticScryfallJson);
        var outputPath = tmp.Path("modern-cards.json.gz");

        var log = new StringWriter();
        var exit = await ExportModernCardsCommand.RunAsync(
            inputPath, outputPath, log, BoltOnlySanityCheck);
        exit.Should().Be(0, log.ToString());

        var rows = ReadGzippedSeed(outputPath);
        rows.Select(r => r.Name).Should().BeEquivalentTo(new[]
        {
            "Lightning Bolt",
            "Path to Exile",
        });
        rows.Should().NotContain(r => r.Name == "Ancestral Recall");
        rows.Should().NotContain(r => r.Name == "Bridge from Below");
    }

    [Fact]
    public async Task Run_DedupesByName_PrefersMostRecentReprint()
    {
        using var tmp = new TempFiles();
        var inputPath = tmp.WriteText("scryfall.json", SyntheticScryfallJson);
        var outputPath = tmp.Path("modern-cards.json.gz");

        var log = new StringWriter();
        var exit = await ExportModernCardsCommand.RunAsync(
            inputPath, outputPath, log, BoltOnlySanityCheck);
        exit.Should().Be(0, log.ToString());

        var rows = ReadGzippedSeed(outputPath);
        var bolt = rows.Single(r => r.Name == "Lightning Bolt");
        bolt.ScryfallId.Should().Be("id-bolt",
            because: "the 2021-06-18 printing should win over the 2010-07-16 one");
        bolt.OracleText.Should().NotContain("old wording");
    }

    [Fact]
    public async Task Run_FlagsImplementedNamesFromRegistry()
    {
        using var tmp = new TempFiles();
        var inputPath = tmp.WriteText("scryfall.json", SyntheticScryfallJson);
        var outputPath = tmp.Path("modern-cards.json.gz");

        var log = new StringWriter();
        var exit = await ExportModernCardsCommand.RunAsync(
            inputPath, outputPath, log, BoltOnlySanityCheck);
        exit.Should().Be(0, log.ToString());

        var rows = ReadGzippedSeed(outputPath);
        // Path to Exile has a [CardName] factory in Majik.Core and so
        // must come back implemented; Lightning Bolt currently doesn't.
        rows.Single(r => r.Name == "Path to Exile").IsImplemented.Should().BeTrue();
        rows.Single(r => r.Name == "Lightning Bolt").IsImplemented.Should().BeFalse();
    }

    [Fact]
    public async Task Run_VerifiesViaEmbeddedCardRepositoryRoundTrip()
    {
        // Append Forest + Mountain so the verification step's basic-land
        // sanity checks pass against the synthetic seed.
        var withBasics = AppendCards(SyntheticScryfallJson, new[]
        {
            BasicLandJson("Forest",   "G", "({T}: Add {G}.)"),
            BasicLandJson("Mountain", "R", "({T}: Add {R}.)"),
        });

        using var tmp = new TempFiles();
        var inputPath = tmp.WriteText("scryfall.json", withBasics);
        var outputPath = tmp.Path("modern-cards.json.gz");

        var log = new StringWriter();
        // Drive the default canonical sanity checks (Lightning Bolt +
        // Forest + Mountain) so this test pins the production verification
        // path rather than the unit-test override.
        var exit = await ExportModernCardsCommand.RunAsync(inputPath, outputPath, log);
        exit.Should().Be(0, log.ToString());

        log.ToString().Should().Contain("verification OK");
    }

    [Fact]
    public async Task Run_MissingInputFile_ReturnsExitCodeOne()
    {
        var log = new StringWriter();
        var exit = await ExportModernCardsCommand.RunAsync(
            "/tmp/definitely-does-not-exist-9e4f3.json",
            "/tmp/should-never-be-written.json.gz",
            log);
        exit.Should().Be(1);
        log.ToString().Should().Contain("error: input not found");
    }

    [Fact]
    public void PrefersReplacement_PicksHigherIsoDate()
    {
        ExportModernCardsCommand.PrefersReplacement("2010-01-01", "2024-01-01")
            .Should().BeTrue();
        ExportModernCardsCommand.PrefersReplacement("2024-01-01", "2010-01-01")
            .Should().BeFalse();
        ExportModernCardsCommand.PrefersReplacement("", "2010-01-01")
            .Should().BeTrue("an empty existing date should yield to any dated row");
        ExportModernCardsCommand.PrefersReplacement("2010-01-01", "")
            .Should().BeFalse("a missing candidate date should never displace");
    }

    [Fact]
    public void LoadImplementedNames_IncludesBasicLandsAndKnownFactory()
    {
        var names = ExportModernCardsCommand.LoadImplementedNames();

        names.Should().Contain("Forest", "basic lands have inline fallbacks in NamedCardFactory");
        names.Should().Contain("Path to Exile",
            "PathToExileFactory carries [CardName(\"Path to Exile\")]");
        names.Should().NotContain("Definitely Not A Real Card");
    }

    // ----- helpers -----

    private static List<SeedRow> ReadGzippedSeed(string path)
    {
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<List<SeedRow>>(
            gz,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private sealed record SeedRow(
        string Name,
        string? ManaCost,
        string? TypeLine,
        string? OracleText,
        string? Power,
        string? Toughness,
        int? Loyalty,
        string? Colors,
        string? ColorIdentity,
        int? Cmc,
        bool IsImplemented,
        string? ScryfallId);

    private static string AppendCards(string json, IEnumerable<string> additions)
    {
        var trimmed = json.TrimEnd().TrimEnd(']').TrimEnd();
        var sb = new StringBuilder(trimmed);
        foreach (var entry in additions)
        {
            sb.Append(',');
            sb.Append(entry);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string BasicLandJson(string name, string color, string oracle) => $$"""
        {
          "id": "id-{{name.ToLowerInvariant()}}",
          "name": "{{name}}",
          "type_line": "Basic Land — {{name}}",
          "oracle_text": "{{oracle}}",
          "colors": [],
          "color_identity": ["{{color}}"],
          "cmc": 0.0,
          "legalities": { "modern": "legal" },
          "released_at": "2024-01-01"
        }
        """;

    private sealed class TempFiles : IDisposable
    {
        private readonly string _dir =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "majik-export-tests-" + Guid.NewGuid().ToString("N"));

        public TempFiles() => Directory.CreateDirectory(_dir);

        public string Path(string name) =>
            System.IO.Path.Combine(_dir, name);

        public string WriteText(string name, string contents)
        {
            var p = Path(name);
            File.WriteAllText(p, contents);
            return p;
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
