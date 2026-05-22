using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Per-card chapter effects wired in <see cref="SagaBinder"/> for
/// Fable of the Mirror-Breaker and The Legend of Roku. Tests cover
/// only what is wired today; deferred chapters are documented in
/// SagaBinder xmldoc.
/// </summary>
public class SagaBinderNamedCardsTests
{
    private static (Player owner, Enchantment saga) MakeSaga(string name)
    {
        var owner = new Player("Alice", 20);
        var saga = new Enchantment(name, "2R",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Saga })
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        owner.Zones.Battlefield.AddCard(saga);
        return (owner, saga);
    }

    private static CardEntity MakeEntity(string name) =>
        new()
        {
            ScryfallId = Guid.NewGuid().ToString(),
            Name = name,
            TypeLine = "Enchantment — Saga",
            OracleText = "",
            Colors = "",
            ColorIdentity = "",
            Keywords = "",
            Legalities = "",
        };

    [Fact]
    public void Fable_ChapterI_CreatesGoblinShamanToken()
    {
        var (owner, saga) = MakeSaga(
            "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        saga.SagaState!.AdvanceAndChapter(); // chapter I

        var tokens = owner.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();
        tokens.Should().HaveCount(1);
        tokens[0].Name.Should().Be("Goblin Shaman");
        tokens[0].Power.Should().Be(2);
        tokens[0].Toughness.Should().Be(2);
        tokens[0].HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        tokens[0].HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    [Fact]
    public void Fable_ChapterII_DiscardsUpToTwoAndDrawsThatMany()
    {
        var (owner, saga) = MakeSaga(
            "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        // Stack hand: 3 cards. Library: 5 cards.
        for (var i = 0; i < 3; i++)
            owner.Zones.Hand.AddCard(new Card($"h{i}", ""));
        for (var i = 0; i < 5; i++)
            owner.Zones.Library.AddCard(new Card($"l{i}", ""));

        saga.SagaState!.AdvanceAndChapter(); // I (spawns token, no hand impact)
        saga.SagaState.AdvanceAndChapter();  // II

        owner.Zones.Graveyard.GetCards().Count().Should().Be(2);
        // Hand started at 3, discarded 2, drew 2 → still 3.
        owner.Zones.Hand.GetCards().Count().Should().Be(3);
        owner.Zones.Library.GetCards().Count().Should().Be(3);
    }

    [Fact]
    public void Roku_ChapterI_ExilesTopThreeOfLibrary()
    {
        var (owner, saga) = MakeSaga("The Legend of Roku // Avatar Roku");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        for (var i = 0; i < 7; i++)
            owner.Zones.Library.AddCard(new Card($"l{i}", ""));

        saga.SagaState!.AdvanceAndChapter(); // I

        owner.Zones.Exile.GetCards().Count().Should().Be(3);
        owner.Zones.Library.GetCards().Count().Should().Be(4);
    }

    [Fact]
    public void Roku_ChapterII_AddsOneRedManaToPool()
    {
        var (owner, saga) = MakeSaga("The Legend of Roku // Avatar Roku");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II

        // v1 deterministic choice: {R}.
        owner.ManaPool.Red.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Fable_FrontFaceNameAlsoBinds()
    {
        // Lookups via DbCardRepository alias the front-face name to the
        // composite row, but SagaBinder is also called against the bare
        // front-face name in some test paths — keep both cases wired.
        var (owner, saga) = MakeSaga("Fable of the Mirror-Breaker");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        saga.SagaState!.AdvanceAndChapter();
        owner.Zones.Battlefield.GetCards().OfType<Creature>()
            .Any(c => c.IsToken && c.Name == "Goblin Shaman")
            .Should().BeTrue();
    }
}
