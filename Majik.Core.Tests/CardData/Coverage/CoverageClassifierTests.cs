using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Coverage;
using Majik.Core.CardData.Database;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Coverage;

/// <summary>
/// Fixture-based smoke tests for the engine-coverage classifier. Verifies
/// each <see cref="CoverageTier"/> against a representative card row. Uses
/// an in-memory <see cref="ICardRepository"/> so tests stay hermetic — no
/// dependency on the user's local SQLite cards.db.
/// </summary>
public class CoverageClassifierTests
{
    private static (CoverageClassifier classifier, FakeRepo repo) Build(
        IEnumerable<CardEntity> entities,
        IEnumerable<string>? namedFactoryNames = null)
    {
        var repo = new FakeRepo(entities);
        var factory = new ScryfallCardFactory(repo);
        var stub = new Player("Synth", 20);
        var names = new HashSet<string>(
            namedFactoryNames ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);
        return (new CoverageClassifier(factory, stub, names), repo);
    }

    [Fact]
    public void NamedFactory_ShortCircuits_OnNameMatch()
    {
        var (cls, _) = Build(
            new[] { new CardEntity { Name = "Lightning Bolt", TypeLine = "Instant", OracleText = "Lightning Bolt deals 3 damage to any target." } },
            namedFactoryNames: new[] { "Lightning Bolt" });

        cls.Classify(new CardEntity { Name = "Lightning Bolt", TypeLine = "Instant", OracleText = "deals 3" })
            .Should().Be(CoverageTier.NamedFactory);
    }

    [Fact]
    public void SpellBound_Tier_When_LookupSpellDefinition_NonNull()
    {
        // "Deals 3 damage to any target" is a long-standing template the
        // OracleSpellBinder registry matches. We don't assert which
        // template wins; just that SpellBound classifies above unimpl.
        var bolt = new CardEntity
        {
            Name = "Fake Bolt",
            TypeLine = "Instant",
            ManaCost = "{R}",
            OracleText = "Fake Bolt deals 3 damage to any target.",
        };
        var (cls, _) = Build(new[] { bolt });
        cls.Classify(bolt).Should().Be(CoverageTier.SpellBound);
    }

    [Fact]
    public void Vanilla_Creature_No_OracleText()
    {
        var bear = new CardEntity
        {
            Name = "Test Bear",
            TypeLine = "Creature — Bear",
            ManaCost = "{1}{G}",
            Power = "2",
            Toughness = "2",
            OracleText = "",
        };
        var (cls, _) = Build(new[] { bear });
        cls.Classify(bear).Should().Be(CoverageTier.Vanilla);
    }

    [Fact]
    public void KeywordOnly_Creature_With_FlyingMarker()
    {
        // "Wind Drake"-style: keyword in Keywords JSON + oracle text is
        // exactly the keyword name. KeywordBinder attaches a marker, so
        // abilities.Count > 0; oracle text is keyword-only → KeywordOnly tier.
        var drake = new CardEntity
        {
            Name = "Test Drake",
            TypeLine = "Creature — Drake",
            ManaCost = "{2}{U}",
            Power = "2",
            Toughness = "2",
            OracleText = "Flying",
            Keywords = "[\"Flying\"]",
        };
        var (cls, _) = Build(new[] { drake });
        cls.Classify(drake).Should().Be(CoverageTier.KeywordOnly);
    }

    [Fact]
    public void Unimplemented_NonKeyword_TextOnly_Creature()
    {
        var unknown = new CardEntity
        {
            Name = "Fictional Wizard",
            TypeLine = "Creature — Human Wizard",
            ManaCost = "{2}{U}",
            Power = "1",
            Toughness = "3",
            // Real text the engine has no factory / template / keyword for.
            OracleText = "Whenever you cast a quoogle, scry 7 and gain the moon.",
            Keywords = "[]",
        };
        var (cls, _) = Build(new[] { unknown });
        cls.Classify(unknown).Should().Be(CoverageTier.Unimplemented);
    }

    [Fact]
    public void Unimplemented_Instant_With_No_Template()
    {
        var weird = new CardEntity
        {
            Name = "Quoogle Bomb",
            TypeLine = "Sorcery",
            ManaCost = "{X}{R}{R}",
            OracleText = "Target opponent reveals seven cards at random.",
        };
        var (cls, _) = Build(new[] { weird });
        cls.Classify(weird).Should().Be(CoverageTier.Unimplemented);
    }

    [Fact]
    public void DiscoverNamedFactoryNames_Includes_ShippedFactories()
    {
        // The reflection-based discovery should find at least the named
        // factories we know exist in the compiled assembly.
        var names = CoverageClassifier.DiscoverNamedFactoryNames();
        names.Should().Contain("Abrupt Decay", "shipped factories are scanned via reflection");
        names.Count.Should().BeGreaterThan(50, "the engine ships hundreds of [CardName] factories");
    }

    [Theory]
    [InlineData("Flying", "[\"Flying\"]", true)]
    [InlineData("Flying, vigilance", "[\"Flying\", \"Vigilance\"]", true)]
    [InlineData("Flying (Some reminder text in parens.)", "[\"Flying\"]", true)]
    [InlineData("Whenever this attacks, draw a card.", "[]", false)]
    [InlineData("Trample\nHaste", "[\"Trample\", \"Haste\"]", true)]
    public void IsKeywordOnlyOracleText_Matches(string oracle, string keywordsJson, bool expected)
    {
        var entity = new CardEntity
        {
            Name = "T",
            TypeLine = "Creature — Bear",
            OracleText = oracle,
            Keywords = keywordsJson,
        };
        CoverageClassifier.IsKeywordOnlyOracleText(entity).Should().Be(expected);
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
