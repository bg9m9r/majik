using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Server.Tests.Decks;

/// <summary>
/// Verifies that <see cref="GameFacade.Create"/> runs the full binder
/// pipeline (KeywordBinder, OracleManaBinder, AffinityBinder, SagaBinder,
/// OracleTriggeredAbilityBinder) when an <see cref="ICardRepository"/> is
/// supplied.
/// </summary>
public class GameFacadeBinderPipelineTests
{
    // -----------------------------------------------------------------------
    // Shared fake repo — mirrors the pattern in RealDeckLoaderBasicLandTests
    // -----------------------------------------------------------------------

    private sealed class FakeCardRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _cards =
            new(StringComparer.OrdinalIgnoreCase);

        public CardEntity? GetByName(string name)
            => _cards.TryGetValue(name, out var c) ? c : null;

        public IReadOnlyList<CardEntity> Search(
            string? q, bool io, int l,
            IReadOnlyList<string>? colors = null,
            IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null)
            => throw new NotImplementedException();

        public bool IsImplemented(string name) => _cards.ContainsKey(name);
        public void SetImplemented(string n, bool v) => throw new NotImplementedException();

        /// <summary>Seed a card with the given oracle / keyword JSON.</summary>
        public void Add(
            string name,
            string typeLine,
            string keywordsJson = "[]",
            string? oracleText = null,
            string manaCost = "")
        {
            _cards[name] = new CardEntity
            {
                Name = name,
                ScryfallId = Guid.NewGuid().ToString(),
                ManaCost = manaCost,
                TypeLine = typeLine,
                Keywords = keywordsJson,
                OracleText = oracleText,
                Set = "TST",
                CollectorNumber = "1",
                IsImplemented = true,
            };
        }
    }

    // -----------------------------------------------------------------------
    // Helper: build a minimal 60-card deck list
    // -----------------------------------------------------------------------

    private static List<ICard> Deck60(ICard named, ICard filler, int namedCount = 4)
    {
        var deck = new List<ICard>();
        for (var i = 0; i < namedCount; i++)
        {
            // Clone filler cards so we don't re-use the same instance
            deck.Add(named);
        }
        for (var i = 0; i < 60 - namedCount; i++)
        {
            // Build fresh Land instances as padding
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));
        }
        return deck;
    }

    // -----------------------------------------------------------------------
    // With repo: KeywordBinder attaches keyword abilities from JSON array
    // -----------------------------------------------------------------------

    [Fact]
    public void GameFacade_Create_WithCardRepo_AttachesKeywordAbilitiesFromJsonArray()
    {
        var repo = new FakeCardRepo();
        repo.Add("Hawk of Skies", "Creature — Bird", keywordsJson: """["Flying","Vigilance"]""");

        var hawk = new Creature("Hawk of Skies", "1W", 2, 2, null, null);
        repo.Add("Forest", "Basic Land — Forest");

        var deck = new List<ICard> { hawk };
        // Pad to 60
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        GameFacade.Create("Alice", "Bob", deck, new List<ICard>(), cardRepo: repo);

        var keywords = hawk.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Vigilance");
    }

    // -----------------------------------------------------------------------
    // With repo: OracleManaBinder.Bind fires for a known basic land
    // -----------------------------------------------------------------------

    [Fact]
    public void GameFacade_Create_WithCardRepo_BasicLand_StillGetsManaAbility()
    {
        var repo = new FakeCardRepo();
        repo.Add("Forest", "Basic Land — Forest",
            oracleText: "({T}: Add {G}.)",
            keywordsJson: "[]");

        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        var deck = new List<ICard> { forest };
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        GameFacade.Create("Alice", "Bob", deck, new List<ICard>(), cardRepo: repo);

        // OracleManaBinder.Bind parses the oracle text; basic-land heuristic also
        // fires. Either way at least one mana ability should be present.
        forest.Abilities.OfType<IManaAbility>().Should().NotBeEmpty(
            "Forest should have a tap-for-green mana ability");
    }

    // -----------------------------------------------------------------------
    // Without repo: basic-land path still fires, non-basics don't crash
    // -----------------------------------------------------------------------

    [Fact]
    public void GameFacade_Create_WithoutCardRepo_BasicLandStillGetsManaAbility()
    {
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        var bear = new Creature("Grizzly Bears", "1G", 2, 2, null, null);

        var deck = new List<ICard> { forest, bear };
        for (var i = 2; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        // No cardRepo — falls back to BindBasicLandMana, should not throw.
        var act = () => GameFacade.Create("Alice", "Bob", deck, new List<ICard>());
        act.Should().NotThrow();

        forest.Abilities.OfType<IManaAbility>().Should().ContainSingle(
            "Forest should have exactly one mana ability via basic-land path");
    }

    // -----------------------------------------------------------------------
    // Without repo: non-basic cards receive no abilities and don't crash
    // -----------------------------------------------------------------------

    [Fact]
    public void GameFacade_Create_WithoutCardRepo_NonBasicCard_HasNoAbilities()
    {
        var bear = new Creature("Grizzly Bears", "1G", 2, 2, null, null);
        var deck = new List<ICard> { bear };
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        GameFacade.Create("Alice", "Bob", deck, new List<ICard>());

        // No repo → BindBasicLandMana fires, which is a no-op for non-land cards.
        bear.Abilities.Should().BeEmpty("creature should have no abilities without a card repo");
    }

    // -----------------------------------------------------------------------
    // With repo: card not found in repo falls back gracefully
    // -----------------------------------------------------------------------

    [Fact]
    public void GameFacade_Create_WithCardRepo_UnknownCard_FallsBackToBasicLandPath()
    {
        // Repo is empty — "Mystery Card" won't be found.
        var repo = new FakeCardRepo();

        var mystery = new Creature("Mystery Card", "3", 2, 2, null, null);
        var deck = new List<ICard> { mystery };
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        var act = () => GameFacade.Create("Alice", "Bob", deck, new List<ICard>(), cardRepo: repo);
        act.Should().NotThrow("unknown cards should degrade gracefully to the basic-land fallback");

        // No mana ability added to a creature — just verifying no exception.
        mystery.Abilities.OfType<IManaAbility>().Should().BeEmpty();
    }
}
