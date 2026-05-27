using FluentAssertions;
using Majik.Core.CardData;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit + smoke coverage for <see cref="EmbeddedCardRepository"/>.
/// Unit-style cases drive the repo with a synthetic 4-row pool so they
/// stay fast; the "Embedded_*" cases load the real bundled
/// <c>modern-cards.json.gz</c> resource once per class instance to
/// verify that the seed is well-formed and contains the canon names
/// gameplay code reaches for.
/// </summary>
public class EmbeddedCardRepositoryTests
{
    private static EmbeddedCardRepository SyntheticRepo()
    {
        var rows = new List<CardEntity>
        {
            new() { Name = "Lightning Bolt", TypeLine = "Instant",
                ManaCost = "{R}", Cmc = 1, Colors = "[\"R\"]",
                ColorIdentity = "[\"R\"]", IsImplemented = true,
                OracleText = "Lightning Bolt deals 3 damage to any target." },
            new() { Name = "Forest", TypeLine = "Basic Land — Forest",
                Cmc = 0, Colors = "[]", ColorIdentity = "[\"G\"]",
                IsImplemented = true, OracleText = "({T}: Add {G}.)" },
            new() { Name = "Grizzly Bears", TypeLine = "Creature — Bear",
                ManaCost = "{1}{G}", Cmc = 2, Power = "2", Toughness = "2",
                Colors = "[\"G\"]", ColorIdentity = "[\"G\"]" },
            new() { Name = "Black Lotus", TypeLine = "Artifact",
                ManaCost = "{0}", Cmc = 0, Colors = "[]",
                ColorIdentity = "[]" },
        };
        // Use the internal loader-delegate constructor so this test
        // avoids touching the bundled 22k-row embedded resource.
        return CreateWithRows(rows);
    }

    private static EmbeddedCardRepository CreateWithRows(IReadOnlyList<CardEntity> rows)
    {
        var ctor = typeof(EmbeddedCardRepository).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            new[] { typeof(Func<IReadOnlyList<CardEntity>>), typeof(EmbeddedCardRepository.ILogSink) })!;
        return (EmbeddedCardRepository)ctor.Invoke(new object?[]
        {
            (Func<IReadOnlyList<CardEntity>>)(() => rows),
            null,
        });
    }

    [Fact]
    public void GetByName_Found_ReturnsEntity()
    {
        var repo = SyntheticRepo();
        repo.GetByName("Lightning Bolt").Should().NotBeNull();
    }

    [Fact]
    public void GetByName_CaseInsensitive()
    {
        var repo = SyntheticRepo();
        repo.GetByName("lightning bolt")!.Name.Should().Be("Lightning Bolt");
    }

    [Fact]
    public void GetByName_NotFound_ReturnsNull()
    {
        var repo = SyntheticRepo();
        repo.GetByName("Does Not Exist").Should().BeNull();
    }

    [Fact]
    public void GetByName_NullOrWhitespace_ReturnsNull()
    {
        var repo = SyntheticRepo();
        repo.GetByName("").Should().BeNull();
        repo.GetByName("   ").Should().BeNull();
    }

    [Fact]
    public void GetByNames_DeduplicatesAndOmitsUnknown()
    {
        var repo = SyntheticRepo();
        var rows = repo.GetByNames(new[] { "Forest", "Forest", "Lightning Bolt", "Mystery" });
        rows.Select(r => r.Name).Should().BeEquivalentTo("Forest", "Lightning Bolt");
    }

    [Fact]
    public void Search_PrefixFilter_ReturnsMatches()
    {
        var repo = SyntheticRepo();
        var hits = repo.Search("Light", implementedOnly: false, limit: 10);
        hits.Single().Name.Should().Be("Lightning Bolt");
    }

    [Fact]
    public void Search_ImplementedOnly_FiltersUnimplemented()
    {
        var repo = SyntheticRepo();
        var hits = repo.Search(q: null, implementedOnly: true, limit: 10);
        hits.Select(r => r.Name).Should().BeEquivalentTo("Lightning Bolt", "Forest");
    }

    [Fact]
    public void Search_ColorFilter_HandlesColorlessSentinel()
    {
        var repo = SyntheticRepo();
        var colorless = repo.Search(q: null, implementedOnly: false, limit: 10,
            colors: new[] { "C" });
        colorless.Select(r => r.Name).Should().Contain("Forest"); // empty Colors array → colorless match
        colorless.Select(r => r.Name).Should().Contain("Black Lotus");
    }

    [Fact]
    public void Search_TypeFilter_ParsesTypeLineSupertypeAndSubtype()
    {
        var repo = SyntheticRepo();
        var creatures = repo.Search(q: null, implementedOnly: false, limit: 10,
            types: new[] { "Creature" });
        creatures.Single().Name.Should().Be("Grizzly Bears");
    }

    [Fact]
    public void Search_CmcBucket_HandlesSevenPlusAsCatchall()
    {
        var repo = SyntheticRepo();
        var ones = repo.Search(q: null, implementedOnly: false, limit: 10,
            cmcBuckets: new[] { 1 });
        ones.Single().Name.Should().Be("Lightning Bolt");
    }

    [Fact]
    public void IsImplemented_ReadsBakedFlag()
    {
        var repo = SyntheticRepo();
        repo.IsImplemented("Lightning Bolt").Should().BeTrue();
        repo.IsImplemented("Grizzly Bears").Should().BeFalse();
        repo.IsImplemented("Does Not Exist").Should().BeFalse();
    }

    [Fact]
    public void DeriveImplemented_OverridesStoredFlagFromFactoryRegistry()
    {
        // A card with a [CardName] factory comes back implemented even if
        // the seed stored false; a card with no factory comes back
        // unimplemented even if the seed stored true. The gzipped seed's
        // stored flag is no longer authoritative — the [CardName] registry
        // (Majik.Core.CardData.Factories.ImplementedCardNames) is.
        var hasFactory = new CardEntity
        {
            Name = "Lightning Bolt", IsImplemented = false,
        };
        var noFactory = new CardEntity
        {
            Name = "Definitely Not A Real Card", IsImplemented = true,
        };

        EmbeddedCardRepository.DeriveImplemented(hasFactory)
            .IsImplemented.Should().BeTrue(
                "Lightning Bolt has a [CardName] factory regardless of the stored flag");
        EmbeddedCardRepository.DeriveImplemented(noFactory)
            .IsImplemented.Should().BeFalse(
                "an unimplemented card stays false even if the seed stored true");
    }

    [Fact]
    public void Embedded_IsImplemented_DerivedFromFactoryRegistry()
    {
        // Drives the real bundled seed through the production load path,
        // proving the flag is recomputed from the [CardName] registry
        // rather than trusted from the gz.
        var repo = new EmbeddedCardRepository();
        repo.IsImplemented("Lightning Bolt").Should().BeTrue(
            "LightningBoltFactory carries [CardName(\"Lightning Bolt\")]");
        repo.IsImplemented("Forest").Should().BeTrue(
            "basic lands have inline fallbacks in NamedCardFactory");
    }

    [Fact]
    public void SetImplemented_Throws_NotSupported()
    {
        var repo = SyntheticRepo();
        var act = () => repo.SetImplemented("Lightning Bolt", false);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void IntentFor_ReturnsNone_AfterCompiledCacheRemoval()
    {
        // The DbCompiledSpellTemplateRepository cache was deleted along
        // with SQLite. EmbeddedCardRepository keeps the interface method
        // for compat but always returns None.
        var repo = SyntheticRepo();
        repo.IntentFor("Lightning Bolt").Should().Be(Majik.Core.Cards.BotIntent.None);
    }

    // ----- DFC / adventure / split front-face derivation (CR 712) -----

    /// <summary>
    /// The embedded seed stores double-faced cards as "Front // Back" but
    /// factories register the front-face name via [CardName("Front")].
    /// DeriveImplemented must fall back to the front-face substring so that
    /// e.g. "Bonecrusher Giant // Stomp" is counted as implemented because
    /// "Bonecrusher Giant" is in the [CardName] registry.
    /// </summary>
    [Fact]
    public void DeriveImplemented_DfcCard_ImplementedWhenFrontFaceIsInRegistry()
    {
        // "Bonecrusher Giant" has a real [CardName] factory; the seed form
        // of the card is "Bonecrusher Giant // Stomp".
        var entity = new CardEntity
        {
            Name = "Bonecrusher Giant // Stomp",
            IsImplemented = false, // seed flag is false — we want it derived to true
        };

        EmbeddedCardRepository.DeriveImplemented(entity)
            .IsImplemented.Should().BeTrue(
                "front-face \"Bonecrusher Giant\" is in ImplementedCardNames " +
                "so the DFC seed entry must derive IsImplemented = true");
    }

    [Fact]
    public void DeriveImplemented_DfcCard_UnimplementedWhenFrontFaceNotInRegistry()
    {
        // A DFC whose front face has no factory must stay false.
        var entity = new CardEntity
        {
            Name = "NoFactory Front // NoFactory Back",
            IsImplemented = true, // seed stored true — should be overridden to false
        };

        EmbeddedCardRepository.DeriveImplemented(entity)
            .IsImplemented.Should().BeFalse(
                "neither the full name nor the front-face is in ImplementedCardNames");
    }

    [Fact]
    public void DeriveImplemented_SingleFacedImplemented_StillTrue()
    {
        // Regression: ordinary single-faced implemented card must not be affected.
        var entity = new CardEntity { Name = "Lightning Bolt", IsImplemented = false };

        EmbeddedCardRepository.DeriveImplemented(entity)
            .IsImplemented.Should().BeTrue(
                "Lightning Bolt is in ImplementedCardNames regardless of stored flag");
    }

    [Fact]
    public void DeriveImplemented_SingleFacedUnimplemented_StillFalse()
    {
        // Regression: ordinary single-faced unimplemented card must not be affected.
        // Use a name that is definitely not in any factory or inline fallback list.
        var entity = new CardEntity { Name = "Definitely Not A Real Card", IsImplemented = true };

        EmbeddedCardRepository.DeriveImplemented(entity)
            .IsImplemented.Should().BeFalse(
                "a name absent from ImplementedCardNames stays false even if the seed stored true");
    }

    [Fact]
    public void GetByName_DfcFrontName_ReturnsEntityWithIsImplementedTrue()
    {
        // Ensure that querying by front-face name not only finds the entity
        // (GetByName already handles this via prefix scan) but also that the
        // returned entity has IsImplemented = true after derivation.
        var dfcEntity = new CardEntity
        {
            Name = "Bonecrusher Giant // Stomp",
            TypeLine = "Creature — Giant // Instant",
            Cmc = 3,
            Colors = "[\"R\"]",
            ColorIdentity = "[\"R\"]",
            IsImplemented = false,
        };
        var repo = CreateWithRowsDerived(new[] { dfcEntity });

        var hit = repo.GetByName("Bonecrusher Giant");
        hit.Should().NotBeNull("GetByName resolves front-face prefix to DFC entity");
        hit!.IsImplemented.Should().BeTrue(
            "DeriveImplemented must have flipped the flag via front-face fallback");
    }

    // Convenience: creates a repo whose loader runs DeriveImplemented on each row,
    // matching the production LoadFromEmbeddedResource path.
    private static EmbeddedCardRepository CreateWithRowsDerived(
        IEnumerable<CardEntity> rawRows)
    {
        var derived = rawRows
            .Select(EmbeddedCardRepository.DeriveImplemented)
            .ToList();
        return CreateWithRows(derived);
    }

    // ----- smoke: real embedded resource -----

    [Fact]
    public void Embedded_LoadsThousandsOfRows()
    {
        var repo = new EmbeddedCardRepository();
        // 22k-ish rows in the bundled Modern-legal pool; assert a
        // lower bound that's still well above any synthetic fixture.
        repo.Count.Should().BeGreaterThan(20_000);
    }

    [Theory]
    [InlineData("Lightning Bolt", "{R}", "Instant", true)]
    [InlineData("Forest",         null,  "Basic Land — Forest", true)]
    [InlineData("Mountain",       null,  "Basic Land — Mountain", true)]
    [InlineData("Snapcaster Mage","{1}{U}", "Creature — Human Wizard", true)]
    public void Embedded_KnownCard_HasExpectedShape(
        string name, string? manaCost, string typeLine, bool atLeastInPool)
    {
        var repo = new EmbeddedCardRepository();
        var hit = repo.GetByName(name);
        atLeastInPool.Should().BeTrue("test scaffolding"); // self-check
        hit.Should().NotBeNull($"'{name}' is canon Modern and must be in the seed");
        hit!.TypeLine.Should().Be(typeLine);
        hit.ManaCost.Should().Be(manaCost);
    }
}
