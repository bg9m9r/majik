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
/// Unit tests for <see cref="CurateFactory"/>.
///
/// Curate (Theros Beyond Death, {1}{U}, Instant):
///   "Surveil 2. Draw a card." — surveil 2 then draw 1, via the shared
///   declarative surveil_self / draw_card verbs.
///
/// Covers identity, named dispatch, default-surveil resolve (both peeked
/// cards milled, the next card drawn), agent-keeps-both resolve, and the
/// empty-library draw-from-empty flag.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class CurateTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void Curate_HasExpectedShape()
    {
        var card = CurateFactory.Create(_alice);

        card.Name.Should().Be("Curate");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Curate()
    {
        var card = NamedCardFactory.Create("Curate", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Curate");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Curate_Resolve_DefaultSurveil_MillsBothPeeked_ThenDrawsNext()
    {
        // Library: [t1, t2, next]. Default surveil sends both peeked cards
        // (t1, t2) to the graveyard; the draw then pulls `next` into hand.
        var t1 = SeedLibraryCard("T1");
        var t2 = SeedLibraryCard("T2");
        var next = SeedLibraryCard("Next");

        var effect = CurateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { t1, t2 });
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { next });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        t1.Zone.Should().Be(ZoneType.Graveyard);
        next.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Curate_Resolve_AgentKeepsBothOnTop_DrawsOriginalTop()
    {
        // Library: [t1, t2, third]. Agent keeps both on top in order; the
        // draw pulls `t1` into hand, leaving [t2, third].
        var t1 = SeedLibraryCard("T1");
        var t2 = SeedLibraryCard("T2");
        var third = SeedLibraryCard("Third");

        var agent = new ScriptedAgent();
        agent.QueueSurveilDecision(new SurveilAction.SurveilDecision(
            ToGraveyard: Array.Empty<ICard>(),
            TopOrder: new ICard[] { t1, t2 }));
        AgentRegistry.Set(_alice, agent);

        var effect = CurateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { t1 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { t2, third });
        t1.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Curate_Resolve_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        var effect = CurateFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
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
