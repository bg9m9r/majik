using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Decks;
using Xunit;

namespace Majik.Server.Tests.Decks;

public class DeckValidationServiceTests
{
    /// <summary>In-memory fake of ICardRepository. Configured per-test.</summary>
    private sealed class FakeCardRepo : ICardRepository
    {
        public Dictionary<string, CardEntity> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Implemented { get; } = new(StringComparer.OrdinalIgnoreCase);

        public CardEntity? GetByName(string name) =>
            Cards.TryGetValue(name, out var c) ? c : null;

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
            => throw new NotImplementedException();

        public bool IsImplemented(string name) => Implemented.Contains(name);

        public void SetImplemented(string name, bool value) =>
            throw new NotImplementedException();

        public void Add(string name, string typeLine, bool implemented = true)
        {
            Cards[name] = new CardEntity
            {
                Name = name,
                ScryfallId = Guid.NewGuid().ToString(),
                ManaCost = "",
                TypeLine = typeLine,
                Set = "TST",
                CollectorNumber = "1",
                IsImplemented = implemented,
            };
            if (implemented) Implemented.Add(name);
        }
    }

    private static Deck DeckOf(List<DeckCardEntry> main, List<DeckCardEntry>? side = null) => new()
    {
        Id = Guid.NewGuid(),
        OwnerSub = "stub-alice",
        Name = "Test",
        Mainboard = main,
        Sideboard = side ?? new List<DeckCardEntry>(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static List<DeckCardEntry> ManyBasicForest(int count) =>
        new() { new() { Name = "Forest", Count = count } };

    private static FakeCardRepo BasicRepo()
    {
        var r = new FakeCardRepo();
        r.Add("Forest", "Basic Land — Forest");
        r.Add("Mountain", "Basic Land — Mountain");
        r.Add("Lightning Bolt", "Instant");
        r.Add("Grizzly Bears", "Creature — Bear");
        r.Add("Hill Giant", "Creature — Giant");
        return r;
    }

    [Fact]
    public void Validate_Valid60CardMonoGreen_Passes()
    {
        var repo = BasicRepo();
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 24 },
            new() { Name = "Grizzly Bears", Count = 4 },
            new() { Name = "Hill Giant", Count = 4 },
            // pad with more basics
            new() { Name = "Mountain", Count = 28 },
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_UndersizedMainboard_Errors()
    {
        var repo = BasicRepo();
        var deck = DeckOf(ManyBasicForest(48));
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("48 cards") && e.Contains("minimum 60"));
    }

    [Fact]
    public void Validate_OversizedSideboard_Errors()
    {
        var repo = BasicRepo();
        var deck = DeckOf(
            main: ManyBasicForest(60),
            side: new List<DeckCardEntry> { new() { Name = "Lightning Bolt", Count = 4 }, new() { Name = "Grizzly Bears", Count = 4 }, new() { Name = "Hill Giant", Count = 4 }, new() { Name = "Mountain", Count = 16 } });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Should().Contain(e => e.Contains("sideboard has 28") && e.Contains("maximum 15"));
    }

    [Fact]
    public void Validate_FiveCopiesOfNonBasic_Errors()
    {
        var repo = BasicRepo();
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 55 },
            new() { Name = "Grizzly Bears", Count = 5 },
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Should().Contain(e => e.Contains("Grizzly Bears") && e.Contains("5 copies"));
    }

    [Fact]
    public void Validate_TwentyFourBasicLands_AllowedDespite4OfLimit()
    {
        var repo = BasicRepo();
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 24 },
            new() { Name = "Grizzly Bears", Count = 4 },
            new() { Name = "Hill Giant", Count = 4 },
            new() { Name = "Mountain", Count = 28 },
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CombinedMainSideExceedsFour_Errors()
    {
        var repo = BasicRepo();
        var deck = DeckOf(
            main: new List<DeckCardEntry>
            {
                new() { Name = "Forest", Count = 56 },
                new() { Name = "Lightning Bolt", Count = 3 },
                new() { Name = "Hill Giant", Count = 1 },
            },
            side: new List<DeckCardEntry>
            {
                new() { Name = "Lightning Bolt", Count = 2 },
            });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Should().Contain(e => e.Contains("Lightning Bolt") && e.Contains("5"));
    }

    [Fact]
    public void Validate_UnknownCard_Errors()
    {
        var repo = BasicRepo();
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 56 },
            new() { Name = "Made Up Card", Count = 4 },
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Should().Contain(e => e.Contains("unknown card") && e.Contains("Made Up Card"));
    }

    [Fact]
    public void Validate_UnimplementedCard_Errors()
    {
        var repo = BasicRepo();
        repo.Add("Black Lotus", "Artifact", implemented: false);
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 56 },
            new() { Name = "Black Lotus", Count = 4 },
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Should().Contain(e => e.Contains("not implemented") && e.Contains("Black Lotus"));
    }

    [Fact]
    public void Validate_ZeroCount_Errors()
    {
        var repo = BasicRepo();
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 60 },
            new() { Name = "Grizzly Bears", Count = 0 },
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Should().Contain(e => e.Contains("Grizzly Bears") && e.Contains("count must be at least 1"));
    }

    [Fact]
    public void Validate_DuplicateMainboardEntries_Errors()
    {
        var repo = BasicRepo();
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 56 },
            new() { Name = "Grizzly Bears", Count = 2 },
            new() { Name = "Grizzly Bears", Count = 2 },
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Should().Contain(e => e.Contains("duplicate entry in mainboard"));
    }

    [Fact]
    public void Validate_TokenType_Errors()
    {
        var repo = BasicRepo();
        repo.Add("Soldier Token", "Token Creature — Soldier");
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 56 },
            new() { Name = "Soldier Token", Count = 4 },
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Should().Contain(e => e.Contains("Soldier Token") && e.Contains("type not legal"));
    }

    [Fact]
    public void Validate_AggregatesMultipleErrors()
    {
        var repo = BasicRepo();
        var deck = DeckOf(new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 40 }, // mainboard too small
            new() { Name = "Grizzly Bears", Count = 5 }, // 4-of violation
        });
        var svc = new DeckValidationService(repo);

        var result = svc.Validate(deck);

        result.Errors.Count.Should().BeGreaterThanOrEqualTo(2);
    }
}
