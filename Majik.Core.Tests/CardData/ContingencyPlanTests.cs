using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ContingencyPlanFactory"/>.
///
/// Contingency Plan (Eldritch Moon, {1}{U}, Sorcery):
///   "Surveil 5." — the single declarative surveil_self(5) verb (CR 701.42).
///   No draw rider; pure deep surveil.
///
/// Covers identity, named dispatch, default-surveil resolve (all peeked cards
/// milled), an agent partition (some milled, some kept on top), and a
/// short-library resolve (surveil tolerates fewer than 5 cards).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class ContingencyPlanTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void ContingencyPlan_HasExpectedShape()
    {
        var card = ContingencyPlanFactory.Create(_alice);

        card.Name.Should().Be("Contingency Plan");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ContingencyPlan()
    {
        var card = NamedCardFactory.Create("Contingency Plan", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Contingency Plan");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ContingencyPlan_Resolve_DefaultSurveil_MillsTopFive()
    {
        // Library: [c1..c6]. Default surveil sends all FIVE peeked cards to
        // the graveyard; the 6th stays on top. No draw clause.
        var cards = new[] { "C1", "C2", "C3", "C4", "C5", "C6" }
            .Select(SeedLibraryCard).ToArray();

        var effect = ContingencyPlanFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Equal(cards.Take(5));
        _alice.Zones.Library.GetCards().Should().Equal(new[] { cards[5] });
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ContingencyPlan_Resolve_AgentPartition_MillsSomeKeepsRest()
    {
        // Library: [c1..c5]. Agent mills c1, c2; keeps c3, c4, c5 on top in
        // that order. No card is drawn.
        var c = new[] { "C1", "C2", "C3", "C4", "C5" }.Select(SeedLibraryCard).ToArray();

        var agent = new ScriptedAgent();
        agent.QueueSurveilDecision(new SurveilAction.SurveilDecision(
            ToGraveyard: new ICard[] { c[0], c[1] },
            TopOrder: new ICard[] { c[2], c[3], c[4] }));
        AgentRegistry.Set(_alice, agent);

        var effect = ContingencyPlanFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { c[0], c[1] });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { c[2], c[3], c[4] });
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ContingencyPlan_Resolve_ShortLibrary_SurveilsWhatExists()
    {
        // Only two cards; surveil 5 peeks just those two. Default mills both.
        var c1 = SeedLibraryCard("C1");
        var c2 = SeedLibraryCard("C2");

        var effect = ContingencyPlanFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { c1, c2 });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
