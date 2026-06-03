using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Per-chapter effects for The Restoration of Eiganjo // Architect of
/// Restoration (CR 714), wired in <see cref="SagaBinder"/>. Mirrors the
/// shape of <see cref="SagaBinderNamedCardsTests"/>.
///   I  — Search your library for a basic Plains card, reveal it, put it
///        into your hand, then shuffle.
///   II — You may discard a card. When you do, return target permanent card
///        with mana value 2 or less from your graveyard to the battlefield
///        tapped.
///   III— Exile this Saga, then return it to the battlefield transformed
///        (Architect of Restoration).
/// </summary>
public class RestorationOfEiganjoSagaTests
{
    private const string CombinedName =
        "The Restoration of Eiganjo // Architect of Restoration";

    private static readonly string OracleText =
        "(As this Saga enters and after your draw step, add a lore counter.)\n" +
        "I — Search your library for a basic Plains card, reveal it, put it into your hand, then shuffle.\n" +
        "II — You may discard a card. When you do, return target permanent card with mana value 2 or less from your graveyard to the battlefield tapped.\n" +
        "III — Exile this Saga, then return it to the battlefield transformed.";

    private static (Player owner, Enchantment saga) MakeSaga()
    {
        var owner = new Player("Alice", 20);
        var saga = new Enchantment(CombinedName, "2W",
            subtypes: new[] { CardSubtype.Saga })
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        owner.Zones.Battlefield.AddCard(saga);
        return (owner, saga);
    }

    private static CardEntity MakeEntity() =>
        new()
        {
            ScryfallId = Guid.NewGuid().ToString(),
            Name = CombinedName,
            TypeLine = "Enchantment — Saga",
            OracleText = OracleText,
            Colors = "W",
            ColorIdentity = "W",
            Keywords = "",
            Legalities = "",
        };

    [Fact]
    public void ChapterI_SearchesBasicPlainsIntoHand_ThenShuffles()
    {
        var (owner, saga) = MakeSaga();
        SagaBinder.Bind(saga, MakeEntity()).Should().BeTrue();

        // Library: a basic Plains + some non-matching cards.
        var plains = new Card("Plains", "",
            cardTypes: new[] { CardType.Land },
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        owner.Zones.Library.AddCard(new Card("Island Basic", "",
            cardTypes: new[] { CardType.Land },
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island }));
        owner.Zones.Library.AddCard(plains);
        owner.Zones.Library.AddCard(new Card("Filler", ""));

        saga.SagaState!.AdvanceAndChapter(); // chapter I

        owner.Zones.Hand.GetCards().Should().Contain(plains,
            "chapter I searches for a basic Plains and puts it into hand");
        owner.Zones.Library.GetCards().Should().NotContain(plains);
    }

    [Fact]
    public void ChapterII_DiscardThenReanimateTargetMv2OrLess_Tapped()
    {
        var (owner, saga) = MakeSaga();
        SagaBinder.Bind(saga, MakeEntity()).Should().BeTrue();

        // Agent discards one card for chapter II's "you may discard a card".
        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueFromHand(cs => cs[0]);
        Majik.Core.Players.Agents.AgentRegistry.Set(owner, agent);

        try
        {
            var toDiscard = new Card("ToDiscard", "");
            owner.Zones.Hand.AddCard(toDiscard);

            // Graveyard: a mv-2 permanent card (eligible) + an expensive one.
            var bear = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = owner, Controller = owner, Zone = ZoneType.Graveyard };
            owner.Zones.Graveyard.AddCard(bear);
            var dragon = new Creature("Big Dragon", "5RR", 5, 5)
            { Owner = owner, Controller = owner, Zone = ZoneType.Graveyard };
            owner.Zones.Graveyard.AddCard(dragon);

            saga.SagaState!.AdvanceAndChapter(); // I (Plains search — empty lib, no-op)
            saga.SagaState.AdvanceAndChapter();  // II

            owner.Zones.Hand.GetCards().Should().BeEmpty("the card was discarded");
            owner.Zones.Graveyard.GetCards().Should().Contain(toDiscard,
                "the discarded card lands in the graveyard");
            owner.Zones.Battlefield.GetCards().Should().Contain(bear,
                "the mv-2 permanent is returned to the battlefield");
            owner.Zones.Battlefield.GetCards().Should().NotContain(dragon,
                "the mv-5 permanent is NOT eligible (mv 2 or less only)");
            bear.IsTapped.Should().BeTrue("it returns to the battlefield tapped");
        }
        finally
        {
            Majik.Core.Players.Agents.AgentRegistry.Remove(owner);
        }
    }

    [Fact]
    public void ChapterII_AgentDeclines_NoReanimation()
    {
        var (owner, saga) = MakeSaga();
        SagaBinder.Bind(saga, MakeEntity()).Should().BeTrue();

        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueFromHand((ICard?)null); // decline the optional discard
        Majik.Core.Players.Agents.AgentRegistry.Set(owner, agent);

        try
        {
            owner.Zones.Hand.AddCard(new Card("KeepMe", ""));
            var bear = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = owner, Controller = owner, Zone = ZoneType.Graveyard };
            owner.Zones.Graveyard.AddCard(bear);

            saga.SagaState!.AdvanceAndChapter(); // I
            saga.SagaState.AdvanceAndChapter();  // II — declined

            owner.Zones.Hand.GetCards().Should().HaveCount(1, "decline → no discard");
            owner.Zones.Battlefield.GetCards().Should().NotContain(bear,
                "no discard → no reanimation (the 'when you do' is unmet)");
        }
        finally
        {
            Majik.Core.Players.Agents.AgentRegistry.Remove(owner);
        }
    }

    [Fact]
    public void ChapterIII_TransformsIntoArchitectOfRestoration()
    {
        var (owner, saga) = MakeSaga();
        SagaBinder.Bind(saga, MakeEntity()).Should().BeTrue();

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II
        saga.SagaState.AdvanceAndChapter();  // III — transform

        owner.Zones.Exile.GetCards().Should().Contain(saga,
            "chapter III exiles the Saga front face");

        var architect = owner.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.Name == "Architect of Restoration");
        architect.Should().NotBeNull("chapter III returns the Saga transformed");
        architect!.Power.Should().Be(3);
        architect.Toughness.Should().Be(4);
        architect.HasSubtype(CardSubtype.Fox).Should().BeTrue();
        architect.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        saga.SagaState.Should().BeNull("SagaState is cleared so the sacrifice SBA does not fire");
    }
}
