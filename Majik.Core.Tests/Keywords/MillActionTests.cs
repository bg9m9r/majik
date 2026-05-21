using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

public class MillActionTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Apply_MillsTopN_LeavingRemainder()
    {
        var cards = new[] { Card("A"), Card("B"), Card("C"), Card("D"), Card("E") };
        foreach (var c in cards) { _alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library); }

        var milled = MillAction.Apply(_alice, 3);

        milled.Select(c => c.Name).Should().Equal("A", "B", "C");
        _alice.Zones.Library.GetCards().Select(c => c.Name).Should().Equal("D", "E");
        _alice.Zones.Graveyard.GetCards().Select(c => c.Name).Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Apply_CountExceedsLibrary_MillsAllRemaining()
    {
        var cards = new[] { Card("A"), Card("B") };
        foreach (var c in cards) { _alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library); }

        var milled = MillAction.Apply(_alice, 5);

        milled.Select(c => c.Name).Should().Equal("A", "B");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Select(c => c.Name).Should().Equal("A", "B");
    }

    [Fact]
    public void Apply_Zero_IsNoOp()
    {
        var a = Card("A");
        _alice.Zones.Library.AddCard(a); a.SetZone(ZoneType.Library);

        var milled = MillAction.Apply(_alice, 0);

        milled.Should().BeEmpty();
        _alice.Zones.Library.GetCards().Select(c => c.Name).Should().Equal("A");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Apply_Negative_IsNoOp()
    {
        var a = Card("A");
        _alice.Zones.Library.AddCard(a); a.SetZone(ZoneType.Library);

        var milled = MillAction.Apply(_alice, -3);

        milled.Should().BeEmpty();
        _alice.Zones.Library.GetCards().Select(c => c.Name).Should().Equal("A");
    }

    [Fact]
    public void Apply_EmptyLibrary_ReturnsEmpty()
    {
        var milled = MillAction.Apply(_alice, 3);
        milled.Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    private static Card Card(string name) => new(name, "");
}
