using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

public class SurveilActionTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Peek_ReturnsTopN()
    {
        var cards = new[] { Card("A"), Card("B"), Card("C"), Card("D") };
        foreach (var c in cards) { _alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library); }

        var top2 = SurveilAction.Peek(_alice, 2);

        top2.Select(c => c.Name).Should().Equal("A", "B");
    }

    [Fact]
    public void Apply_AllToGraveyard_LibraryShortenedGraveyardFilled()
    {
        var (a, b, c, d) = (Card("A"), Card("B"), Card("C"), Card("D"));
        foreach (var x in new[] { a, b, c, d }) { _alice.Zones.Library.AddCard(x); x.SetZone(ZoneType.Library); }

        SurveilAction.Apply(_alice, 2, new SurveilAction.SurveilDecision(
            ToGraveyard: new[] { a, b },
            TopOrder: System.Array.Empty<ICard>()));

        _alice.Zones.Library.GetCards().Select(x => x.Name).Should().Equal("C", "D");
        _alice.Zones.Graveyard.GetCards().Select(x => x.Name).Should().Equal("A", "B");
    }

    [Fact]
    public void Apply_PartialToGraveyard_RestStackedOnTop()
    {
        var (a, b, c, d) = (Card("A"), Card("B"), Card("C"), Card("D"));
        foreach (var x in new[] { a, b, c, d }) { _alice.Zones.Library.AddCard(x); x.SetZone(ZoneType.Library); }

        SurveilAction.Apply(_alice, 2, new SurveilAction.SurveilDecision(
            ToGraveyard: new[] { a },
            TopOrder: new[] { b }));

        // B stays on top, A goes to graveyard, then C, D follow
        _alice.Zones.Library.GetCards().Select(x => x.Name).Should().Equal("B", "C", "D");
        _alice.Zones.Graveyard.GetCards().Select(x => x.Name).Should().Equal("A");
    }

    [Fact]
    public void Apply_ZeroToGraveyard_ReordersTopOnly()
    {
        var (a, b, c, d) = (Card("A"), Card("B"), Card("C"), Card("D"));
        foreach (var x in new[] { a, b, c, d }) { _alice.Zones.Library.AddCard(x); x.SetZone(ZoneType.Library); }

        SurveilAction.Apply(_alice, 2, new SurveilAction.SurveilDecision(
            ToGraveyard: System.Array.Empty<ICard>(),
            TopOrder: new[] { b, a })); // swap so B on top

        _alice.Zones.Library.GetCards().Select(x => x.Name).Should().Equal("B", "A", "C", "D");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Apply_PartitionMismatch_Throws()
    {
        var a = Card("A");
        _alice.Zones.Library.AddCard(a); a.SetZone(ZoneType.Library);

        var act = () => SurveilAction.Apply(_alice, 1,
            new SurveilAction.SurveilDecision(
                ToGraveyard: System.Array.Empty<ICard>(),
                TopOrder: System.Array.Empty<ICard>()));

        act.Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Peek_NLargerThanLibrary_ReturnsAvailable()
    {
        var a = Card("A");
        _alice.Zones.Library.AddCard(a); a.SetZone(ZoneType.Library);

        var top = SurveilAction.Peek(_alice, 5);
        top.Select(c => c.Name).Should().Equal("A");
    }

    private static Card Card(string name) => new(name, "");
}
