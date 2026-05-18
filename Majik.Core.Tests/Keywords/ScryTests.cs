using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

public class ScryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Peek_ReturnsTopN()
    {
        var cards = new[] { Card("A"), Card("B"), Card("C"), Card("D") };
        foreach (var c in cards) { _alice.Zones.Library.AddCard(c); c.Zone = ZoneType.Library; }

        var top2 = ScryAction.Peek(_alice, 2);

        top2.Select(c => c.Name).Should().Equal("A", "B");
    }

    [Fact]
    public void Apply_BottomsAll_LibraryReordersCorrectly()
    {
        var (a, b, c, d) = (Card("A"), Card("B"), Card("C"), Card("D"));
        foreach (var x in new[] { a, b, c, d }) { _alice.Zones.Library.AddCard(x); x.Zone = ZoneType.Library; }

        ScryAction.Apply(_alice, 2, new ScryAction.ScryDecision(
            ToBottom: new[] { a, b },
            TopOrder: System.Array.Empty<ICard>()));

        _alice.Zones.Library.GetCards().Select(x => x.Name)
            .Should().Equal("C", "D", "A", "B");
    }

    [Fact]
    public void Apply_KeepsTopReorder_ReversesIfRequested()
    {
        var (a, b, c, d) = (Card("A"), Card("B"), Card("C"), Card("D"));
        foreach (var x in new[] { a, b, c, d }) { _alice.Zones.Library.AddCard(x); x.Zone = ZoneType.Library; }

        ScryAction.Apply(_alice, 2, new ScryAction.ScryDecision(
            ToBottom: System.Array.Empty<ICard>(),
            TopOrder: new[] { b, a })); // swap so B on top

        _alice.Zones.Library.GetCards().Select(x => x.Name)
            .Should().Equal("B", "A", "C", "D");
    }

    [Fact]
    public void Apply_PartitionMismatch_Throws()
    {
        var a = Card("A");
        _alice.Zones.Library.AddCard(a); a.Zone = ZoneType.Library;

        var act = () => ScryAction.Apply(_alice, 1,
            new ScryAction.ScryDecision(
                ToBottom: System.Array.Empty<ICard>(),
                TopOrder: System.Array.Empty<ICard>()));

        act.Should().Throw<System.InvalidOperationException>();
    }

    private static Card Card(string name) => new(name, "");
}
