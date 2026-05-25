using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
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

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();

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

    // -----------------------------------------------------------------------
    // With repo + shock land: ShockLandBinder registers onto facade.Replacements
    // so the ETB replacement fires when the card moves to the battlefield.
    // CR 614 — replacement effects modify how events would occur.
    // -----------------------------------------------------------------------

    [Fact]
    public void GameFacade_Create_AttachesShockLandReplacement_WhenRepoAndShockEntityPresent()
    {
        var repo = new FakeCardRepo();
        const string ShockOracleText =
            "({T}: Add {B} or {G}.)\n" +
            "As this land enters, you may pay 2 life. If you don't, it enters tapped.";
        repo.Add("Overgrown Tomb", "Land — Swamp Forest", oracleText: ShockOracleText);
        repo.Add("Forest", "Basic Land — Forest");

        // Alice starts at 20 life — above the "pay 2" threshold so the
        // replacement should elect to pay 2 life and let the land enter untapped.
        var tomb = new Land("Overgrown Tomb",
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Swamp, CardSubtype.Forest });

        var deck = new List<ICard> { tomb };
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        var facade = GameFacade.Create("Alice", "Bob", deck, new List<ICard>(), cardRepo: repo);

        // Verify the replacement is wired: push a ZoneMoveIntent for Overgrown
        // Tomb moving from Hand → Battlefield through facade.Replacements and
        // check that the replacement fires (life deducted or EntersTapped flag set).
        // ShockLandReplacement.Applies checks the intent zones, not card.Zone,
        // so we don't need internal Zone access here.
        var alice = new Player("Alice", 20);
        tomb.ChangeOwner(alice);

        var intent = new ZoneMoveIntent(tomb, ZoneType.Hand, ZoneType.Battlefield, Controller: alice);
        var result = facade.Replacements.Apply(intent);

        // With 20 life (> 2), policy pays 2 life and lets land enter untapped.
        result.Should().NotBeNull("the replacement should not cancel the move");
        alice.LifeTotal.Should().Be(18, "shock land pays 2 life when controller has > 2 life (CR 702.18)");
        result!.EntersTapped.Should().BeFalse("land enters untapped when life was paid");
    }

    [Fact]
    public void GameFacade_Create_AttachesShockLandReplacement_EntersTapped_WhenControllerAtLowLife()
    {
        var repo = new FakeCardRepo();
        const string ShockOracleText =
            "({T}: Add {U} or {R}.)\n" +
            "As this land enters, you may pay 2 life. If you don't, it enters tapped.";
        repo.Add("Steam Vents", "Land — Island Mountain", oracleText: ShockOracleText);
        repo.Add("Forest", "Basic Land — Forest");

        var steamVents = new Land("Steam Vents",
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Island, CardSubtype.Mountain });

        var deck = new List<ICard> { steamVents };
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        var facade = GameFacade.Create("Alice", "Bob", deck, new List<ICard>(), cardRepo: repo);

        // Controller at exactly 2 life — policy skips paying and enters tapped.
        var alice = new Player("Alice", 2);
        steamVents.ChangeOwner(alice);

        var intent = new ZoneMoveIntent(steamVents, ZoneType.Hand, ZoneType.Battlefield, Controller: alice);
        var result = facade.Replacements.Apply(intent);

        result.Should().NotBeNull();
        alice.LifeTotal.Should().Be(2, "no life paid when controller is at 2 or below");
        result!.EntersTapped.Should().BeTrue("land enters tapped when life was not paid");
    }

    [Fact]
    public void GameFacade_Create_WithCardRepo_NonShockLand_NoReplacementRegistered()
    {
        var repo = new FakeCardRepo();
        repo.Add("Forest", "Basic Land — Forest",
            oracleText: "({T}: Add {G}.)",
            keywordsJson: "[]");

        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        var deck = new List<ICard> { forest };
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        var facade = GameFacade.Create("Alice", "Bob", deck, new List<ICard>(), cardRepo: repo);

        // A basic Forest has no shock clause — intent passes through unchanged.
        var alice = new Player("Alice", 20);
        forest.ChangeOwner(alice);

        var intent = new ZoneMoveIntent(forest, ZoneType.Hand, ZoneType.Battlefield, Controller: alice);
        var result = facade.Replacements.Apply(intent);

        result.Should().NotBeNull();
        alice.LifeTotal.Should().Be(20, "no life paid for a basic land");
        result!.EntersTapped.Should().BeFalse("basic land enters untapped");
    }
}
