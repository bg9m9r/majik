using FluentAssertions;
using Majik.Core.CardData.MechanicDeps;
using Xunit;

namespace Majik.Core.Tests.CardData.MechanicDeps;

/// <summary>
/// Unit coverage for the deferral scanner + clusterer pair. Hermetic:
/// fixture text strings are passed directly into
/// <see cref="DeferralScanner.Scan"/>, so the tests do not touch disk.
///
/// The fixtures are designed to exercise the spec scenarios:
/// <list type="bullet">
///   <item>Three deferral comments in one factory → clusterer extracts 3
///         mentions, two matching canonical primitives, one in Other.</item>
///   <item>Registry pattern lookup works for each canonical primitive.</item>
///   <item>Reporting sorts by distinct-factory count descending.</item>
/// </list>
/// </summary>
public class MechanicDepsClusteringTests
{
    // ---------------- Scanner extraction ----------------

    [Fact]
    public void Scanner_ExtractsThreeMentions_TwoCanonical_OneOther()
    {
        // Three xmldoc bullets, each with one deferred-rider line:
        //  - regeneration  → canonical "regeneration"
        //  - library shuffle → canonical "library-shuffle"
        //  - mana-payment   → no canonical primitive → unclustered
        var fixture = """
            /// <summary>
            /// Test card.
            ///
            /// ## Deferred (v1 gaps)
            /// - <b>Regeneration</b>: the "can't be regenerated" rider is
            ///   deferred — engine has no regeneration shield yet.
            /// - <b>Library shuffle</b>: shuffle on tutor resolution is
            ///   deferred (CR 701.20).
            /// - <b>Mana-payment replacement</b>: a payment-replacement
            ///   subsystem is deferred until the cost pipeline is layered.
            /// </summary>
            public static class FoobarFactory { }
            """;

        var scanner = new DeferralScanner();
        var mentions = scanner.Scan("Foobar.cs", "FoobarFactory", fixture);

        mentions.Should().HaveCount(3);
        mentions.Should().AllSatisfy(m =>
        {
            m.FactoryName.Should().Be("FoobarFactory");
            m.Sentence.Should().NotBeNullOrWhiteSpace();
        });

        var clusterer = new MechanicDependencyClusterer();
        var report = clusterer.Cluster(mentions);

        // Two canonical hits.
        var ids = report.Clusters.Select(c => c.PrimitiveId).ToList();
        ids.Should().Contain("regeneration");
        ids.Should().Contain("library-shuffle");

        // The mana-payment bullet has no registry pattern → unclustered.
        report.Unclustered.Should().HaveCount(1);
        report.Unclustered[0].Sentence.Should().Contain("Mana-payment");
    }

    [Fact]
    public void Scanner_SkipsBareSectionHeaders()
    {
        // A factory whose xmldoc has the "## Deferred (v1 gaps)" header
        // followed by NO content bullets should yield zero mentions —
        // the header itself contains a trigger keyword but isn't
        // actionable text.
        var fixture = """
            /// <summary>
            /// Vanilla card.
            /// ## Deferred (v1 gaps)
            /// </summary>
            public static class VanillaFactory { }
            """;

        var scanner = new DeferralScanner();
        var mentions = scanner.Scan("Vanilla.cs", "VanillaFactory", fixture);
        mentions.Should().BeEmpty();
    }

    [Fact]
    public void Scanner_PreservesCompRulesCitation_WithoutSplittingOnDecimal()
    {
        // CR rule numbers contain dots; the sentence splitter must not
        // chop "CR 702.143" mid-citation.
        var fixture = """
            /// <summary>Escape spell.</summary>
            /// Escape cost {2}{B}{B} (CR 702.143) deferred — same gap as
            /// every other graveyard alt-cost spell.
            public static class EscapeFactory { }
            """;

        var scanner = new DeferralScanner();
        var mentions = scanner.Scan("Escape.cs", "EscapeFactory", fixture);

        mentions.Should().HaveCount(1);
        mentions[0].Sentence.Should().Contain("702.143");
        mentions[0].CompRulesCitation.Should().Be("CR 702.143");
    }

    [Fact]
    public void Scanner_InlineComments_AreAlsoScanned()
    {
        // Inline `//` comments inside a method body should be picked up
        // just like xmldoc comments.
        var fixture = """
            public static class FactoryWithInline {
                public static void Build() {
                    // Indestructible rider deferred — same gap as Terminate.
                    var x = 1;
                }
            }
            """;

        var scanner = new DeferralScanner();
        var mentions = scanner.Scan("Inline.cs", "FactoryWithInline", fixture);

        mentions.Should().HaveCount(1);
        mentions[0].Sentence.Should().Contain("Indestructible");
    }

    // ---------------- Registry lookup ----------------

    [Theory]
    [InlineData("Library shuffle (CR 701.20a) deferred — no IZone.Shuffle yet.", "library-shuffle")]
    [InlineData("Regeneration rider is deferred — engine has no shield surface.", "regeneration")]
    [InlineData("Indestructible bypass on destroy intent is deferred.", "indestructible-bypass")]
    [InlineData("Sorcery-speed gate (CR 117.1a) deferred.", "sorcery-speed-gate")]
    [InlineData("Plot (CR 718) alt-cost rider deferred.", "plot")]
    [InlineData("Escape alt-cost (CR 702.143) deferred.", "escape")]
    [InlineData("Splice onto Arcane (CR 702.46) deferred.", "splice-arcane")]
    [InlineData("Token colour identity is deferred — TokenFactory limitation.", "token-colour-identity")]
    [InlineData("Ascend rider deferred (city's blessing).", "ascend")]
    [InlineData("Manifest dread (CR 701.59) deferred until a face-down primitive ships.", "manifest-dread")]
    [InlineData("Disguise rider deferred — Morph plumbing required first.", "disguise-cloak")]
    public void Registry_MapsCanonicalPhrases(string sentence, string expectedPrimitiveId)
    {
        var primitive = MechanicPrimitiveRegistry.Match(sentence);
        primitive.Should().NotBeNull(because: $"sentence '{sentence}' should map to '{expectedPrimitiveId}'");
        primitive!.Id.Should().Be(expectedPrimitiveId);
    }

    [Fact]
    public void Registry_UnknownSentence_ReturnsNull()
    {
        var sentence = "Wibbly wobbly time-y wimey stuff is deferred — see Doctor Who.";
        MechanicPrimitiveRegistry.Match(sentence).Should().BeNull();
    }

    [Fact]
    public void Registry_AllPrimitivesHaveAtLeastOnePattern()
    {
        // Catches an oversight where a new primitive lands in the
        // registry without a single Rx — would be unreachable.
        MechanicPrimitiveRegistry.All.Should().AllSatisfy(p =>
        {
            p.MatchPatterns.Should().NotBeEmpty(because: $"primitive {p.Id} has no patterns");
        });
    }

    // ---------------- Reporting / sorting ----------------

    [Fact]
    public void Report_SortsByDistinctFactoryCountDescending()
    {
        // Build three synthetic mention sets:
        //  - library-shuffle:  3 distinct factories
        //  - regeneration:     2 distinct factories
        //  - sorcery-speed:    1 distinct factory
        var mentions = new List<DeferralMention>
        {
            Mention("FactoryA", "Library shuffle (CR 701.20) deferred."),
            Mention("FactoryB", "Library shuffle (CR 701.20) deferred."),
            Mention("FactoryC", "Library shuffle (CR 701.20) deferred."),
            Mention("FactoryD", "Regeneration rider deferred."),
            Mention("FactoryE", "Regeneration rider deferred."),
            Mention("FactoryF", "Sorcery-speed gate deferred."),
        };

        var report = new MechanicDependencyClusterer().Cluster(mentions);
        report.Clusters.Should().HaveCount(3);
        report.Clusters[0].PrimitiveId.Should().Be("library-shuffle");
        report.Clusters[0].FactoryCount.Should().Be(3);
        report.Clusters[1].PrimitiveId.Should().Be("regeneration");
        report.Clusters[1].FactoryCount.Should().Be(2);
        report.Clusters[2].PrimitiveId.Should().Be("sorcery-speed-gate");
        report.Clusters[2].FactoryCount.Should().Be(1);
    }

    [Fact]
    public void Report_DistinctFactories_DedupedWithinCluster()
    {
        // Two mentions in the SAME factory must count once towards
        // FactoryCount even though MentionCount sees both.
        var mentions = new List<DeferralMention>
        {
            Mention("Dup", "Library shuffle deferred (first xmldoc bullet)."),
            Mention("Dup", "Library shuffle deferred (second inline comment)."),
        };

        var report = new MechanicDependencyClusterer().Cluster(mentions);
        var lib = report.Clusters.Single(c => c.PrimitiveId == "library-shuffle");
        lib.MentionCount.Should().Be(2);
        lib.FactoryCount.Should().Be(1);
    }

    [Fact]
    public void Report_EmptyInput_EmitsEmptyReport()
    {
        var report = new MechanicDependencyClusterer().Cluster(Array.Empty<DeferralMention>());
        report.Clusters.Should().BeEmpty();
        report.Unclustered.Should().BeEmpty();
        report.TotalMentions.Should().Be(0);
    }

    private static DeferralMention Mention(string factory, string sentence) =>
        new(
            FactoryFile: $"{factory}.cs",
            FactoryName: factory,
            LineNumber: 1,
            Sentence: sentence,
            CompRulesCitation: null);
}
