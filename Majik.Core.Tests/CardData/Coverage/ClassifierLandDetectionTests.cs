using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Coverage;
using Majik.Core.CardData.Database;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Coverage;

/// <summary>
/// Regression tests for <see cref="CoverageClassifier"/>'s land branch.
/// Before this fix the classifier rated basic lands and shocklands as
/// <see cref="CoverageTier.Unimplemented"/> because their oracle text
/// is non-empty and their <c>Keywords</c> JSON is empty, which made
/// <see cref="CoverageClassifier.IsKeywordOnlyOracleText"/> return false
/// — even though the engine ships full mana-ability + shock-clause
/// coverage. The fix special-cases the land branch and falls through to
/// a vanilla-shell signal for everything else.
/// </summary>
public class ClassifierLandDetectionTests
{
    private static CoverageClassifier Build(
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

    [Fact]
    public void Island_BasicLand_IsKeywordOnly()
    {
        var island = new CardEntity
        {
            Name = "Island",
            TypeLine = "Basic Land — Island",
            OracleText = "({T}: Add {U}.)",
            Keywords = "[]",
        };
        var cls = Build(new[] { island });
        cls.Classify(island).Should().Be(CoverageTier.KeywordOnly);
    }

    [Fact]
    public void Forest_BasicLand_IsKeywordOnly()
    {
        var forest = new CardEntity
        {
            Name = "Forest",
            TypeLine = "Basic Land — Forest",
            OracleText = "({T}: Add {G}.)",
            Keywords = "[]",
        };
        var cls = Build(new[] { forest });
        cls.Classify(forest).Should().Be(CoverageTier.KeywordOnly);
    }

    [Fact]
    public void Wastes_BasicLand_NoSubtype_IsKeywordOnly()
    {
        // Wastes is "Basic Land" with no subtype line and empty oracle.
        // It still belongs in KeywordOnly: the classifier should rely on
        // the Basic-Land type-line signal alone.
        var wastes = new CardEntity
        {
            Name = "Wastes",
            TypeLine = "Basic Land",
            OracleText = "",
            Keywords = "[]",
        };
        var cls = Build(new[] { wastes });
        cls.Classify(wastes).Should().Be(CoverageTier.KeywordOnly);
    }

    [Fact]
    public void SteamVents_Shockland_IsKeywordOnly()
    {
        var steamVents = new CardEntity
        {
            Name = "Steam Vents",
            TypeLine = "Land — Island Mountain",
            OracleText =
                "({T}: Add {U} or {R}.)\n" +
                "As Steam Vents enters, you may pay 2 life. If you don't, it enters tapped.",
            Keywords = "[]",
        };
        var cls = Build(new[] { steamVents });
        cls.Classify(steamVents).Should().Be(CoverageTier.KeywordOnly);
    }

    [Fact]
    public void WateryGrave_Shockland_IsKeywordOnly()
    {
        var wateryGrave = new CardEntity
        {
            Name = "Watery Grave",
            TypeLine = "Land — Island Swamp",
            OracleText =
                "({T}: Add {U} or {B}.)\n" +
                "As Watery Grave enters, you may pay 2 life. If you don't, it enters tapped.",
            Keywords = "[]",
        };
        var cls = Build(new[] { wateryGrave });
        cls.Classify(wateryGrave).Should().Be(CoverageTier.KeywordOnly);
    }

    [Fact]
    public void BloodCrypt_Shockland_IsKeywordOnly()
    {
        var bloodCrypt = new CardEntity
        {
            Name = "Blood Crypt",
            TypeLine = "Land — Swamp Mountain",
            OracleText =
                "({T}: Add {B} or {R}.)\n" +
                "As Blood Crypt enters, you may pay 2 life. If you don't, it enters tapped.",
            Keywords = "[]",
        };
        var cls = Build(new[] { bloodCrypt });
        cls.Classify(bloodCrypt).Should().Be(CoverageTier.KeywordOnly);
    }

    [Fact]
    public void BloodstainedMire_NamedFactory_StillShortCircuits()
    {
        // Fetch lands have shipped under a [CardName] factory cycle. The
        // refactor's land branch sits AFTER the named-factory check, so
        // these must continue to report NamedFactory.
        var mire = new CardEntity
        {
            Name = "Bloodstained Mire",
            TypeLine = "Land",
            OracleText =
                "{T}, Pay 1 life, Sacrifice Bloodstained Mire: " +
                "Search your library for a Swamp or Mountain card, " +
                "put it onto the battlefield, then shuffle.",
            Keywords = "[]",
        };
        var cls = Build(
            new[] { mire },
            namedFactoryNames: new[] { "Bloodstained Mire" });
        cls.Classify(mire).Should().Be(CoverageTier.NamedFactory);
    }

    [Fact]
    public void Non_Basic_Land_With_No_Oracle_Or_Abilities_Is_Vanilla()
    {
        // A hypothetical land with no oracle text and no factory binding
        // (no Basic supertype, no shock clause). The engine plays it as
        // a colourless do-nothing tapland — Vanilla, not Unimplemented.
        var weirdLand = new CardEntity
        {
            Name = "Test Empty Land",
            TypeLine = "Land",
            OracleText = "",
            Keywords = "[]",
        };
        var cls = Build(new[] { weirdLand });
        cls.Classify(weirdLand).Should().Be(CoverageTier.Vanilla);
    }

    [Fact]
    public void Unimplemented_Lhurgoyf_Creature_Is_Still_Unimplemented()
    {
        // Sanity: the land-branch refactor must not regress the
        // long-tail unimplemented-creature path.
        var goyf = new CardEntity
        {
            Name = "Fictional Goyf",
            TypeLine = "Creature — Lhurgoyf",
            ManaCost = "{1}{G}",
            Power = "*",
            Toughness = "1+*",
            OracleText =
                "Fictional Goyf's power is equal to the number of card types " +
                "among cards in all graveyards and its toughness is equal to " +
                "that number plus 1.",
            Keywords = "[]",
        };
        var cls = Build(new[] { goyf });
        cls.Classify(goyf).Should().Be(CoverageTier.Unimplemented);
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
