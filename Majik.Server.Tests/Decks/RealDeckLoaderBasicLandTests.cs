using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Server.Decks;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Decks;

/// <summary>
/// Verifies that basic lands loaded through RealDeckLoader → GameFacade.Create
/// end up with the correct tap-for-mana ability (Rule 106.1, CR 305.6).
/// </summary>
public class RealDeckLoaderBasicLandTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public RealDeckLoaderBasicLandTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Unit tests for OracleManaBinder.BindBasicLandMana — no MongoDB needed
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Forest", CardSubtype.Forest, "G")]
    [InlineData("Plains", CardSubtype.Plains, "W")]
    [InlineData("Island", CardSubtype.Island, "U")]
    [InlineData("Swamp", CardSubtype.Swamp, "B")]
    [InlineData("Mountain", CardSubtype.Mountain, "R")]
    [InlineData("Wastes", CardSubtype.Wastes, "C")]
    public void BindBasicLandMana_AttachesCorrectColor(string name, CardSubtype subtype, string expectedColor)
    {
        var controller = new Player("Alice", 20);
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });

        OracleManaBinder.BindBasicLandMana(land, controller);

        var mana = land.Abilities.OfType<IManaAbility>().Single();
        mana.Activate().Should().Be(ManaCost.Parse(expectedColor));
    }

    [Fact]
    public void BindBasicLandMana_NonBasicLand_AddsNoAbility()
    {
        var controller = new Player("Alice", 20);
        // A land with Forest subtype but no Basic supertype is not a basic land.
        var land = new Land("Tropical Island", supertypes: null, new[] { CardSubtype.Forest });

        OracleManaBinder.BindBasicLandMana(land, controller);

        land.Abilities.OfType<IManaAbility>().Should().BeEmpty();
    }

    [Fact]
    public void BindBasicLandMana_NoBasicSubtype_AddsNoAbility()
    {
        var controller = new Player("Alice", 20);
        var land = new Land("Undiscovered Paradise", new[] { CardSupertype.Basic }, subtypes: null);

        OracleManaBinder.BindBasicLandMana(land, controller);

        land.Abilities.OfType<IManaAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Integration: GameFacade.Create wires mana abilities from deck cards
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Forest", "Basic Land — Forest", "G")]
    [InlineData("Plains", "Basic Land — Plains", "W")]
    [InlineData("Island", "Basic Land — Island", "U")]
    [InlineData("Swamp", "Basic Land — Swamp", "B")]
    [InlineData("Mountain", "Basic Land — Mountain", "R")]
    [InlineData("Wastes", "Basic Land — Wastes", "C")]
    public void GameFacade_Create_BasicLand_HasManaAbility(string name, string typeLine, string expectedColor)
    {
        // Materialise a card the same way RealDeckLoader does: via TypeLineParser.
        var parsed = TypeLineParser.Parse(typeLine);
        var land = new Land(name, parsed.Supertypes, parsed.Subtypes);

        var deck = new List<ICard> { land };
        var opponent = new List<ICard>();

        // GameFacade.Create assigns owners + attaches basic-land mana abilities.
        var game = GameFacade.Create("Alice", "Bob", deck, opponent);

        var mana = land.Abilities.OfType<IManaAbility>().Single();
        mana.Activate().Should().Be(ManaCost.Parse(expectedColor));
    }

    // -----------------------------------------------------------------------
    // Integration: full loader → GameFacade pipeline (requires MongoDB)
    // -----------------------------------------------------------------------

    private sealed class FakeCardRepo : ICardRepository
    {
        public Dictionary<string, CardEntity> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Implemented { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CardEntity? GetByName(string name) => Cards.TryGetValue(name, out var c) ? c : null;
        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();
        public IReadOnlyList<CardEntity> Search(string? q, bool io, int l,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) => throw new NotImplementedException();
        public bool IsImplemented(string name) => Implemented.Contains(name);
        public void SetImplemented(string n, bool v) => throw new NotImplementedException();

        public void Add(string name, string typeLine)
        {
            Cards[name] = new CardEntity
            {
                Name = name, ScryfallId = Guid.NewGuid().ToString(), ManaCost = "",
                TypeLine = typeLine, Set = "TST", CollectorNumber = "1", IsImplemented = true,
            };
            Implemented.Add(name);
        }
    }

    [Theory]
    [InlineData("Forest", "Basic Land — Forest", "G")]
    [InlineData("Plains", "Basic Land — Plains", "W")]
    [InlineData("Island", "Basic Land — Island", "U")]
    [InlineData("Swamp", "Basic Land — Swamp", "B")]
    [InlineData("Mountain", "Basic Land — Mountain", "R")]
    [InlineData("Wastes", "Basic Land — Wastes", "C")]
    public async Task LoadAsync_BasicLand_GameFacade_HasManaAbility(
        string landName, string typeLine, string expectedColor)
    {
        var repo = new DeckRepository(_fixture.NewDatabase());
        await repo.EnsureIndexesAsync(CancellationToken.None);

        var cards = new FakeCardRepo();
        cards.Add(landName, typeLine);
        // Pad to 60 using another basic land (exempt from the 4-copy rule).
        // Use "Plains" as filler unless we're already testing Plains, then use "Island".
        var fillerName = landName == "Plains" ? "Island" : "Plains";
        var fillerTypeLine = fillerName == "Plains" ? "Basic Land — Plains" : "Basic Land — Island";
        cards.Add(fillerName, fillerTypeLine);

        var validator = new DeckValidationService(cards);
        var loader = new RealDeckLoader(repo, cards, validator);

        var deck = new Deck
        {
            Id = Guid.NewGuid(),
            OwnerSub = "stub-alice",
            Name = "Test",
            Mainboard = new List<DeckCardEntry>
            {
                new() { Name = landName, Count = 24 },
                new() { Name = fillerName, Count = 36 },
            },
            Sideboard = new List<DeckCardEntry>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await repo.InsertAsync(deck, CancellationToken.None);

        var loaded = await loader.LoadAsync(deck.Id.ToString(), CancellationToken.None);

        // Now pass through GameFacade.Create to assign owners + bind abilities.
        var opponent = new List<ICard>();
        GameFacade.Create("Alice", "Bob", loaded, opponent);

        var lands = loaded.OfType<Land>().Take(3).ToList();
        lands.Should().NotBeEmpty();
        foreach (var land in lands)
        {
            land.Abilities.OfType<IManaAbility>()
                .Should().ContainSingle($"{landName} should have exactly one mana ability")
                .Which.Activate().Should().Be(ManaCost.Parse(expectedColor));
        }
    }
}
