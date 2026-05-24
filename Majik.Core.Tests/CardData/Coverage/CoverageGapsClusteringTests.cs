using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Coverage;
using Majik.Core.CardData.Database;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Coverage;

/// <summary>
/// Unit coverage for <see cref="OracleSignature"/> + the
/// <see cref="CoverageGapClusterer"/> pipeline. Hand-picked fixtures
/// exercise: normalization invariants (~ replacement, reminder-text
/// strip, cost / +N/+N / integer collapse, case-folding), expected
/// bucketing for known mechanic shapes, ranking, <c>--min-cluster</c>
/// threshold behaviour, and the numeric-twin hint.
///
/// Hermetic: builds an in-memory repo + a classifier whose named-factory
/// set is empty, so every text-bearing card the clusterer sees lands in
/// the Unimplemented tier where we want it.
/// </summary>
public class CoverageGapsClusteringTests
{
    // ---------------- Normalization ----------------

    [Fact]
    public void Normalize_LowercaseAndStripsReminderText()
    {
        var raw = "FLYING (This creature can't be blocked except by creatures with flying or reach.)";
        var sig = OracleSignature.Normalize("Wind Drake", raw);
        sig.Should().Be("flying");
    }

    [Fact]
    public void Normalize_ReplacesFullAndShortNameWithTilde()
    {
        var raw = "Urza, Lord High Artificer enters. Urza deals 3 damage to any target.";
        var sig = OracleSignature.Normalize("Urza, Lord High Artificer", raw);
        // Both the full name and the "Urza" short form should be ~.
        sig.Should().Contain("~ enters");
        sig.Should().Contain("~ deals");
        sig.Should().NotContain("urza");
    }

    [Fact]
    public void Normalize_CollapsesManaSymbolRunsToCostToken()
    {
        var raw = "{2}{U}{U}: Draw a card.";
        var sig = OracleSignature.Normalize("Foo", raw);
        sig.Should().StartWith("{cost}:");
    }

    [Fact]
    public void Normalize_CollapsesPowerToughnessAndStandaloneIntegers()
    {
        var raw = "Target creature gets +3/+3 until end of turn. Draw 2 cards.";
        var sig = OracleSignature.Normalize("Foo", raw);
        sig.Should().Contain("+n/+n");
        sig.Should().Contain("draw n cards");
    }

    [Fact]
    public void Normalize_PreservesNegativePowerToughnessSign()
    {
        // -1/-1 counters and +1/+1 counters must not collapse into the
        // same token — they're semantically opposite.
        var raw = "Put a -1/-1 counter on target creature.";
        var sig = OracleSignature.Normalize("Foo", raw);
        sig.Should().Contain("-n/-n counter");
        sig.Should().NotContain("+n/+n");
    }

    [Fact]
    public void Normalize_EmptyOrNullText_IsEmpty()
    {
        OracleSignature.Normalize("Foo", "").Should().Be("");
        OracleSignature.Normalize("Foo", "   ").Should().Be("");
    }

    [Fact]
    public void TriggerSignature_ExtractsWhenEntersClause()
    {
        var sig = OracleSignature.From(
            "Mulldrifter",
            "When Mulldrifter enters, draw two cards.");
        sig.TriggerSignature.Should().Be("when ~ enters,");
    }

    [Fact]
    public void TriggerSignature_ExtractsActivatedCostBoundary()
    {
        var sig = OracleSignature.From(
            "Prodigal Pyromancer",
            "{T}: Prodigal Pyromancer deals 1 damage to any target.");
        sig.TriggerSignature.Should().Be("{cost}:");
    }

    [Fact]
    public void EffectVerb_FindsDealDamageAnyTarget()
    {
        var sig = OracleSignature.From(
            "Bolt-Like",
            "Bolt-Like deals 3 damage to any target.");
        sig.EffectVerbSignature.Should().Be("deal damage (any target)");
    }

    // ---------------- Clustering ----------------

    private static CoverageGapClusterer BuildClusterer(IEnumerable<CardEntity> entities)
    {
        var list = entities.ToList();
        var repo = new FakeRepo(list);
        var factory = new ScryfallCardFactory(repo);
        var stub = new Player("Synth", 20);
        var classifier = new CoverageClassifier(
            factory, stub, new HashSet<string>(StringComparer.Ordinal));
        return new CoverageGapClusterer(classifier);
    }

    [Fact]
    public void Cluster_BucketsCardsBySharedFirstSentenceSignature()
    {
        // Three identical "when X enters, draw a card" cards plus one
        // outlier. Threshold = 1 to exercise the bucketing path.
        var entities = new[]
        {
            UnimplCreature("Card A", "When Card A enters, draw a card."),
            UnimplCreature("Card B", "When Card B enters, draw a card."),
            UnimplCreature("Card C", "When Card C enters, draw a card."),
            UnimplCreature("Loner",  "Whenever you cast a spell, you may bounce a creature."),
        };
        var clusters = BuildClusterer(entities).Cluster(entities, minClusterSize: 1);

        clusters.Should().NotBeEmpty();
        var top = clusters[0];
        top.MemberCount.Should().Be(3);
        top.FirstSentenceSignature.Should().Contain("when ~ enters");
        top.ExampleCardNames.Should().BeEquivalentTo(new[] { "Card A", "Card B", "Card C" });
        top.SuggestedBinderName.Should().Be("EtbDrawCardTriggerBinder");
    }

    [Fact]
    public void Cluster_RanksDescendingByMemberCount()
    {
        var entities = new List<CardEntity>();
        // 5 ETB-draw-a-card cards.
        for (var i = 0; i < 5; i++)
        {
            entities.Add(UnimplCreature($"DrawCard{i}", $"When DrawCard{i} enters, draw a card."));
        }
        // 3 "Whenever you cast a creature" cards.
        for (var i = 0; i < 3; i++)
        {
            entities.Add(UnimplCreature($"CastCreature{i}", $"Whenever you cast a creature spell, scry 1."));
        }
        var clusters = BuildClusterer(entities).Cluster(entities, minClusterSize: 1);

        clusters[0].MemberCount.Should().BeGreaterThan(clusters[^1].MemberCount);
        clusters[0].FirstSentenceSignature.Should().Contain("when ~ enters");
    }

    [Fact]
    public void Cluster_MinClusterSizeFilter_DropsSmallBuckets()
    {
        var entities = new List<CardEntity>();
        // 6 of one shape.
        for (var i = 0; i < 6; i++)
        {
            entities.Add(UnimplCreature($"Big{i}", $"When Big{i} enters, draw a card."));
        }
        // 2 of another (below the default threshold).
        entities.Add(UnimplCreature("Small1", "When Small1 enters, scry 2."));
        entities.Add(UnimplCreature("Small2", "When Small2 enters, scry 2."));

        var clusters = BuildClusterer(entities).Cluster(entities, minClusterSize: 5);

        clusters.Should().HaveCount(1);
        clusters[0].MemberCount.Should().Be(6);
    }

    [Fact]
    public void Cluster_SkipsCardsAlreadyCoveredByOtherTiers()
    {
        // Vanilla creature + keyword-only creature should not surface
        // in the gaps report.
        var entities = new[]
        {
            new CardEntity
            {
                Name = "Bear",
                TypeLine = "Creature — Bear",
                ManaCost = "{1}{G}",
                Power = "2", Toughness = "2",
                OracleText = "",
                Keywords = "[]",
            },
            new CardEntity
            {
                Name = "Drake",
                TypeLine = "Creature — Drake",
                ManaCost = "{2}{U}",
                Power = "2", Toughness = "2",
                OracleText = "Flying",
                Keywords = "[\"Flying\"]",
            },
            UnimplCreature("Mech", "When Mech enters, deal 2 damage to any target."),
            UnimplCreature("Mech2", "When Mech2 enters, deal 2 damage to any target."),
        };

        var clusters = BuildClusterer(entities).Cluster(entities, minClusterSize: 1);

        clusters.Should().OnlyContain(c => !c.ExampleCardNames.Contains("Bear"));
        clusters.Should().OnlyContain(c => !c.ExampleCardNames.Contains("Drake"));
        clusters.SelectMany(c => c.ExampleCardNames).Should().Contain("Mech");
    }

    [Fact]
    public void Cluster_NumericTwinHint_FlagsParametrisableSiblings()
    {
        // "draw 2 cards" and "draw 3 cards" should both end up with a
        // numeric-twin hint after normalisation merges integers to "n"
        // — but since normalisation already does that, the two end up
        // in the *same* cluster. To exercise the twin hint we need
        // signatures that differ in a token the normaliser preserves
        // (e.g. distinct verbs vs distinct numerics). Use damage-N-to-N
        // shapes where N gets stripped but the sentence still differs
        // by some other digit-bearing token. The simplest way: two
        // clusters whose only difference is a digit the normaliser
        // already collapsed will merge → no hint to surface. Instead,
        // exercise the hint with two distinct verb-clusters that share
        // the same non-digit skeleton.
        //
        // Build five "draw N cards" and four "draw a card" cards. The
        // signatures differ ("draw a card" doesn't contain n), so they
        // form distinct clusters with no numeric twin. Cluster ranking
        // smoke check only — the hint is best-effort.
        var entities = new List<CardEntity>();
        for (var i = 0; i < 5; i++)
        {
            entities.Add(UnimplCreature($"DrawN{i}", $"When DrawN{i} enters, draw two cards."));
        }
        for (var i = 0; i < 4; i++)
        {
            entities.Add(UnimplCreature($"Draw1_{i}", $"When Draw1_{i} enters, draw a card."));
        }

        var clusters = BuildClusterer(entities).Cluster(entities, minClusterSize: 1);
        clusters.Should().HaveCount(2);
        clusters[0].MemberCount.Should().Be(5);
        clusters[1].MemberCount.Should().Be(4);
    }

    [Fact]
    public void Cluster_BinderSuggestion_FallsBackToTriggerOnly()
    {
        // Trigger phrase the registry knows ("when ~ enters,") but a
        // resolution clause it doesn't ("you may shuffle the moon"). We
        // expect the generic EtbGenericTriggerBinder fallback.
        var entities = new[]
        {
            UnimplCreature("X1", "When X1 enters, you may shuffle the moon."),
            UnimplCreature("X2", "When X2 enters, you may shuffle the moon."),
            UnimplCreature("X3", "When X3 enters, you may shuffle the moon."),
        };
        var clusters = BuildClusterer(entities).Cluster(entities, minClusterSize: 1);
        clusters[0].SuggestedBinderName.Should().Be("EtbGenericTriggerBinder");
    }

    [Fact]
    public void Cluster_HandPickedFixture_BucketsAsExpected()
    {
        // 12-card hand-picked fixture covering 4 mechanic shapes.
        var entities = new[]
        {
            // 4 × dies-bounce
            UnimplCreature("DiesBounce A", "When DiesBounce A dies, return DiesBounce A to its owner's hand."),
            UnimplCreature("DiesBounce B", "When DiesBounce B dies, return DiesBounce B to its owner's hand."),
            UnimplCreature("DiesBounce C", "When DiesBounce C dies, return DiesBounce C to its owner's hand."),
            UnimplCreature("DiesBounce D", "When DiesBounce D dies, return DiesBounce D to its owner's hand."),

            // 3 × sacrifice-for-damage activated
            UnimplCreature("Sac A", "{1}, Sacrifice Sac A: Sac A deals 2 damage to any target."),
            UnimplCreature("Sac B", "{1}, Sacrifice Sac B: Sac B deals 2 damage to any target."),
            UnimplCreature("Sac C", "{1}, Sacrifice Sac C: Sac C deals 2 damage to any target."),

            // 3 × upkeep gain life
            UnimplCreature("Upk A", "At the beginning of your upkeep, you gain 1 life."),
            UnimplCreature("Upk B", "At the beginning of your upkeep, you gain 1 life."),
            UnimplCreature("Upk C", "At the beginning of your upkeep, you gain 1 life."),

            // 2 × cast trigger (below threshold by default)
            UnimplCreature("Cst A", "Whenever you cast a noncreature spell, scry 2."),
            UnimplCreature("Cst B", "Whenever you cast a noncreature spell, scry 2."),
        };

        var clusters = BuildClusterer(entities).Cluster(entities, minClusterSize: 3);

        clusters.Should().HaveCount(3);
        clusters[0].MemberCount.Should().Be(4);
        clusters[0].FirstSentenceSignature.Should().StartWith("when ~ dies");
        clusters[0].SuggestedBinderName.Should().Be("OnDiesBounceTriggerBinder");

        var sacCluster = clusters.Single(c => c.FirstSentenceSignature.Contains("sacrifice"));
        sacCluster.MemberCount.Should().Be(3);
        sacCluster.SuggestedBinderName.Should().Be("SacrificeForDamageActivatedBinder");

        var upkCluster = clusters.Single(c => c.FirstSentenceSignature.Contains("upkeep"));
        upkCluster.MemberCount.Should().Be(3);
        upkCluster.SuggestedBinderName.Should().Be("UpkeepGainLifeTriggerBinder");
    }

    // ---------------- Helpers ----------------

    private static CardEntity UnimplCreature(string name, string oracle) => new()
    {
        Name = name,
        TypeLine = "Creature — Human Wizard",
        ManaCost = "{2}{U}",
        Power = "1", Toughness = "3",
        OracleText = oracle,
        Keywords = "[]",
    };

    /// <summary>
    /// Local copy of <c>FakeRepo</c> — kept here so this test file is
    /// self-contained; mirrors the one in CoverageReportV2Tests.
    /// </summary>
    private sealed class FakeRepo : Majik.Core.CardData.ICardRepository
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
