using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Stock Up (Outlaws of Thunder Junction, {2}{U}).
///
/// Sorcery. Oracle text:
///   "Look at the top five cards of your library. Put two of them into your
///    hand and the rest on the bottom of your library in any order."
///
/// Structural / textual twin of <see cref="DigThroughTimeFactory"/> (look at
/// top N, put 2 into hand, rest on bottom in any order) minus the Delve
/// keyword and with N = 5 instead of 7. CR 701.18 governs the
/// "on the bottom in any order" placement.
/// </summary>
public class StockUpTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void StockUp_Identity()
    {
        var c = StockUpFactory.Create(_alice);

        c.Name.Should().Be("Stock Up");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().Be(_alice);
        c.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StockUp()
    {
        var card = NamedCardFactory.Create("Stock Up", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Stock Up");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{U}");
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public void StockUp_DefaultSelector_HandFirstTwoBottomRest()
    {
        // Pure selector test — no cast flow needed.
        var peeked = Enumerable.Range(0, 5)
            .Select(i => (ICard)new Card($"P{i}", ""))
            .ToList();

        var (toHand, toBottom) = StockUpFactory.DefaultSelector(peeked);

        toHand.Should().HaveCount(2);
        toBottom.Should().HaveCount(3);
        toHand[0].Name.Should().Be("P0");
        toHand[1].Name.Should().Be("P1");
        toBottom[0].Name.Should().Be("P2");
        toBottom[2].Name.Should().Be("P4");
    }

    [Fact]
    public void StockUp_Resolve_PeekFive_HandTwo_BottomThree()
    {
        // Library: [a, b, c, d, e, f]. Default selector sends the first two
        // peeked cards (a, b) to hand; the next three (c, d, e) go to the
        // bottom in peek order. `f` was never peeked and stays where it was.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");
        var e = SeedLibraryCard("E");
        var f = SeedLibraryCard("F");

        var effect = StockUpFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a, b });
        // f kept its position above the bottomed c/d/e.
        _alice.Zones.Library.GetCards().Should().Equal(new[] { f, c, d, e });
        a.Zone.Should().Be(ZoneType.Hand);
        c.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void StockUp_Resolve_CustomSelector_ReordersBottom()
    {
        // Selector keeps c & a in hand and bottoms the rest in a chosen order
        // (e, then b, then d) — exercises the "in any order" clause.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");
        var e = SeedLibraryCard("E");

        StockUpFactory.Selector pick = peeked =>
        {
            var byName = peeked.ToDictionary(x => x.Name);
            return (
                toHand: new[] { byName["C"], byName["A"] },
                toBottom: new[] { byName["E"], byName["B"], byName["D"] });
        };

        var effect = StockUpFactory.BuildResolveEffect(_alice, pick).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { c, a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { e, b, d });
    }

    [Fact]
    public void StockUp_Resolve_EmptyLibrary_GracefulNoOp()
    {
        var effect = StockUpFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void StockUp_Resolve_FewerThanFiveCards_HandsUpToTwo_BottomsRest()
    {
        // Only three cards. Peek returns [a, b, c]; default selector hands
        // a & b, bottoms c. Library ends [c] (single bottomed card).
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");

        var effect = StockUpFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a, b });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { c });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Card SeedLibraryCard(string name)
    {
        var card = new Card(name, "");
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }
}
