using FluentAssertions;
using Majik.Core.CardData;
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
    public void Fable_ChapterI_CreatesTwoTwoRedGoblinToken()
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
        // Scryfall-confirmed: chapter I creates a 2/2 red Goblin Shaman token
        // (both subtypes). "Goblin Shaman" is the token name.
        tokens[0].Name.Should().Be("Goblin Shaman");
        tokens[0].Power.Should().Be(2);
        tokens[0].Toughness.Should().Be(2);
        tokens[0].HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        tokens[0].HasSubtype(CardSubtype.Shaman).Should()
            .BeTrue("Scryfall: the chapter I token is a Goblin Shaman, not just Goblin");
    }

    [Fact]
    public void Fable_ChapterII_DiscardsUpToTwoAndDrawsThatMany()
    {
        var (owner, saga) = MakeSaga(
            "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        // CR 701.7 — "you may discard up to two cards." The agent (resolved
        // via AgentRegistry) is prompted to pick which cards to discard; here
        // it discards two (the front-of-hand cards).
        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueFromHand(cs => cs[0]);
        agent.QueueFromHand(cs => cs[0]);
        Majik.Core.Players.Agents.AgentRegistry.Set(owner, agent);

        try
        {
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
        finally
        {
            Majik.Core.Players.Agents.AgentRegistry.Remove(owner);
        }
    }

    // Deferral #6 — CR 701.7. Fable chapter II is "you MAY discard up to two
    // cards. If you do, draw that many cards." The controller's agent decides
    // how many (0, 1, or 2); exactly that many are drawn. The "may" is honoured
    // — the controller can decline (discard 0 → draw 0).

    [Fact]
    public void Fable_ChapterII_AgentDiscardsZero_DrawsZero_MayHonoured()
    {
        var (owner, saga) = MakeSaga(
            "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        // Scripted "you may" opt-out — the agent declines on the first prompt.
        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueFromHand((Majik.Core.Cards.ICard?)null); // decline → discard 0
        Majik.Core.Players.Agents.AgentRegistry.Set(owner, agent);

        try
        {
            for (var i = 0; i < 3; i++) owner.Zones.Hand.AddCard(new Card($"h{i}", ""));
            for (var i = 0; i < 5; i++) owner.Zones.Library.AddCard(new Card($"l{i}", ""));

            saga.SagaState!.AdvanceAndChapter(); // I
            saga.SagaState.AdvanceAndChapter();  // II — agent declines

            owner.Zones.Graveyard.GetCards().Should().BeEmpty("declined → discard 0");
            owner.Zones.Hand.GetCards().Count().Should().Be(3, "no discard, no draw");
            owner.Zones.Library.GetCards().Count().Should().Be(5, "drew 0 (you may declined)");
        }
        finally
        {
            Majik.Core.Players.Agents.AgentRegistry.Remove(owner);
        }
    }

    [Fact]
    public void Fable_ChapterII_NoAgentRegistered_DefaultsToDeclineDiscardZero()
    {
        var (owner, saga) = MakeSaga(
            "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        // No agent registered and no explicit count override → safe "may"
        // opt-out: discard 0, draw 0 (never auto-discards the controller's hand).
        for (var i = 0; i < 3; i++) owner.Zones.Hand.AddCard(new Card($"h{i}", ""));
        for (var i = 0; i < 5; i++) owner.Zones.Library.AddCard(new Card($"l{i}", ""));

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II — no agent → decline

        owner.Zones.Graveyard.GetCards().Should().BeEmpty("no agent → honour 'may' and discard nothing");
        owner.Zones.Hand.GetCards().Count().Should().Be(3);
        owner.Zones.Library.GetCards().Count().Should().Be(5);
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
        // Lookups via EmbeddedCardRepository alias the front-face name
        // to the composite row, but SagaBinder is also called against
        // the bare front-face name in some test paths — keep both wired.
        var (owner, saga) = MakeSaga("Fable of the Mirror-Breaker");
        SagaBinder.Bind(saga, MakeEntity(saga.Name)).Should().BeTrue();

        saga.SagaState!.AdvanceAndChapter();
        owner.Zones.Battlefield.GetCards().OfType<Creature>()
            .Any(c => c.IsToken && c.Name == "Goblin Shaman")
            .Should().BeTrue("chapter I token is a Goblin Shaman (Scryfall-confirmed)");
    }
}
