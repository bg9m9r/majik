using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ReachThroughMistsFactory"/>.
///
/// Reach Through Mists (Champions of Kamigawa, {U}, Instant — Arcane):
///   "Draw a card." — the single declarative draw_card(1) verb. The Arcane
///   subtype is stamped so the splice-onto-Arcane gate (CR 702.46) can see it.
///
/// Covers identity (incl. the Arcane subtype), named dispatch, the draw
/// resolve, and the empty-library draw-from-empty flag.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class ReachThroughMistsTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void ReachThroughMists_HasExpectedShape()
    {
        var card = ReachThroughMistsFactory.Create(_alice);

        card.Name.Should().Be("Reach Through Mists");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ReachThroughMists_HasArcaneSubtype()
    {
        var card = ReachThroughMistsFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Arcane).Should().BeTrue(
            because: "Reach Through Mists is printed Instant — Arcane (CR 205.3k)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ReachThroughMists()
    {
        var card = NamedCardFactory.Create("Reach Through Mists", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Reach Through Mists");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ReachThroughMists_Resolve_DrawsTopCard()
    {
        var top = SeedLibraryCard("Top");
        var next = SeedLibraryCard("Next");

        var effect = ReachThroughMistsFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { next });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        top.Zone.Should().Be(ZoneType.Hand);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void ReachThroughMists_Resolve_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        var effect = ReachThroughMistsFactory.BuildResolveEffect(_alice).Single();
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
