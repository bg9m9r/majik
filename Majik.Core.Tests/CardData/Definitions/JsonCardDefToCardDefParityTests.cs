using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tests.Snapshots;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Golden-digest parity guard for PLAN 03 Slice 2 — the JSON
/// <see cref="CardDefinition"/> path now deserializes into a
/// <see cref="CardDef"/> (<see cref="CardDefinition.ToCardDef"/>) and routes
/// through the single materializer
/// (<see cref="CardDefRuntime.Build(CardDef, Player, ReplacementBus?)"/>),
/// replacing the old direct-build <c>CardDefinitionFactory</c>.
///
/// <para>
/// The change is <b>behaviour-neutral</b>: every embedded JSON card must
/// build into a runtime <see cref="ICard"/> whose
/// <see cref="SnapshotSummary"/> digest (P/T, types, supertypes, subtypes,
/// abilities — keyword / mana / activated / triggered shapes, costs, effects,
/// counter replacements) <i>plus</i> its derived colours (CR 202.2c colour
/// indicator) is byte-identical to the digest captured from the pre-Slice-2
/// code on <c>origin/main</c>.
/// </para>
///
/// <para>
/// The golden digest covers <b>all</b> embedded
/// <c>Majik.Core.CardData.Cards.*.json</c> resources (one digest per card,
/// keyed by slug, the whole thing concatenated + sorted so the comparison is
/// order-stable). The committed golden lives next to this test at
/// <c>golden/json-carddef-parity.txt</c>; it was generated from the
/// pre-reroute code, so a green run proves the reroute changed nothing about
/// the runtime cards. Set the <c>MAJIK_REGEN_JSON_PARITY=1</c> env var to
/// rewrite it (used once to capture the baseline).
/// </para>
/// </summary>
public class JsonCardDefToCardDefParityTests
{
    private const string ResourcePrefix = "Majik.Core.CardData.Cards.";
    private const string ResourceSuffix = ".json";

    private static readonly Player Alice = new("Alice", 20);

    [Fact]
    public void AllEmbeddedJsonCards_Digest_MatchesGolden()
    {
        var actual = BuildAllDigests();
        var goldenPath = GoldenPath();

        if (Environment.GetEnvironmentVariable("MAJIK_REGEN_JSON_PARITY") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        File.Exists(goldenPath).Should().BeTrue(
            $"the committed golden digest must exist at {goldenPath} " +
            "(regenerate with MAJIK_REGEN_JSON_PARITY=1 against the pre-reroute code)");

        var golden = File.ReadAllText(goldenPath);

        // Byte-identical to the pre-Slice-2 capture — zero behaviour change.
        actual.Should().Be(golden);
    }

    [Fact]
    public void GoldenDigest_CoversManyCards()
    {
        // Sanity: the parity guard must actually exercise the whole JSON pool,
        // not silently shrink to a handful of cards.
        Slugs().Count.Should().BeGreaterThan(150);
    }

    /// <summary>
    /// Build the concatenated, slug-sorted digest of every embedded JSON
    /// card. Each card builds through the production
    /// <see cref="CardDefinitionFactory.Build(CardDefinition, Player, ReplacementBus?)"/>
    /// entry point (now backed by <see cref="CardDefinition.ToCardDef"/>),
    /// with a fresh <see cref="ReplacementBus"/> so counter-replacement
    /// registrations are captured.
    /// </summary>
    private static string BuildAllDigests()
    {
        var sb = new StringBuilder();
        foreach (var slug in Slugs())
        {
            CardDefinition def;
            try
            {
                def = LoadDefinition(slug);
            }
            catch
            {
                // Non-card JSON fixtures embedded under the same folder are
                // skipped — they aren't CardDefinitions and never went through
                // CardDefinitionFactory.
                continue;
            }

            var bus = new ReplacementBus();
            ICard card;
            try
            {
                card = CardDefinitionFactory.Build(def, Alice, bus);
            }
            catch (Exception ex)
            {
                // A card that legitimately could not build before (and so was
                // never shipped) records its failure mode so the digest still
                // pins behaviour. Identical failure text pre/post = neutral.
                sb.Append(slug).Append(" => BUILD-ERROR: ")
                  .Append(ex.GetType().Name).Append(": ").Append(ex.Message)
                  .Append('\n');
                continue;
            }

            var digest = SnapshotSummary.Build(card, bus);
            digest["colors"] = Colors(card);

            sb.Append("==== ").Append(slug).Append(" ====\n");
            sb.Append(SnapshotSummary.Serialize(digest));
        }
        return sb.ToString();
    }

    private static JsonArray Colors(ICard card)
    {
        var arr = new JsonArray();
        foreach (var c in CardColors.GetColors(card)
                     .Select(c => c.ToString())
                     .OrderBy(s => s, StringComparer.Ordinal))
        {
            arr.Add(c);
        }
        return arr;
    }

    private static CardDefinition LoadDefinition(string slug)
    {
        var asm = typeof(CardDefinitionLoader).Assembly;
        var resource = ResourcePrefix + slug + ResourceSuffix;
        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException(resource);
        using var reader = new StreamReader(stream);
        return CardDefinitionLoader.FromJson(reader.ReadToEnd());
    }

    /// <summary>Every embedded JSON card slug, sorted ordinally.</summary>
    private static IReadOnlyList<string> Slugs()
    {
        var asm = typeof(CardDefinitionLoader).Assembly;
        return asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(n => n.Substring(
                ResourcePrefix.Length,
                n.Length - ResourcePrefix.Length - ResourceSuffix.Length))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GoldenPath()
    {
        // Walk up from the test bin dir to the test project root, then into
        // the committed golden folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Majik.Core.Tests.csproj")))
        {
            dir = dir.Parent;
        }
        var root = dir?.FullName
            ?? throw new InvalidOperationException("Could not locate Majik.Core.Tests project root.");
        return Path.Combine(root, "CardData", "Definitions", "golden", "json-carddef-parity.txt");
    }
}
