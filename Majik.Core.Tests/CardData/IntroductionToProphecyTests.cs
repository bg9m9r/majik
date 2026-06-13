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
/// Unit tests for <see cref="IntroductionToProphecyFactory"/>.
///
/// Introduction to Prophecy (Strixhaven, {3}, Sorcery — Lesson):
///   "Scry 2, then draw a card." — same declarative scry_self(2) → draw_card(1)
///   body as Preordain / Deliberate, at the colorless cost {3}.
///
/// Covers identity (incl. mana value 3), named dispatch, default-scry resolve,
/// agent-keeps-both resolve, and the empty-library draw-from-empty flag.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class IntroductionToProphecyTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void IntroductionToProphecy_HasExpectedShape()
    {
        var card = IntroductionToProphecyFactory.Create(_alice);

        card.Name.Should().Be("Introduction to Prophecy");
        card.ManaCost.Should().Be("{3}");
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{3} = mana value 3");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_IntroductionToProphecy()
    {
        var card = NamedCardFactory.Create("Introduction to Prophecy", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Introduction to Prophecy");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{3}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IntroductionToProphecy_Resolve_DefaultScry_BottomsBoth_ThenDraws()
    {
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var effect = IntroductionToProphecyFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { c });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { d, a, b });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        c.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void IntroductionToProphecy_Resolve_AgentKeepsBothOnTop_DrawsOriginalTop()
    {
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a, b }));
        AgentRegistry.Set(_alice, agent);

        var effect = IntroductionToProphecyFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c });
        a.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void IntroductionToProphecy_Resolve_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        var effect = IntroductionToProphecyFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
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
