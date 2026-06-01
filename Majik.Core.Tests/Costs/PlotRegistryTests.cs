using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 718 — Plot. Reusable <see cref="PlotRegistry"/>: plot (exile) from hand
/// at sorcery speed, then cast for free on a LATER turn (never the same turn),
/// once per turn.
/// </summary>
public class PlotRegistryTests
{
    private static Instant InHand(Player owner, string name = "Plotted Spell")
    {
        var card = new Instant(name, "1R") { Owner = owner };
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        return card;
    }

    [Fact]
    public void Plot_ExilesFromHand_AndPaysPlotCost()
    {
        var alice = new Player("Alice", 20);
        var card = InHand(alice);
        var reg = new PlotRegistry();
        alice.AddManaToPool(ManaCost.Parse("1R"));

        var plotted = reg.Plot(card, alice, currentTurn: 3,
            payPlotCost: () => alice.PayMana(ManaCost.Parse("1R")));

        plotted.Should().BeTrue();
        card.Zone.Should().Be(ZoneType.Exile, "the plotted card is exiled");
        alice.Zones.Hand.GetCards().Should().NotContain(card);
        alice.Zones.Exile.GetCards().Should().Contain(card);
        reg.IsPlotted(card).Should().BeTrue();
        reg.TurnPlotted(card).Should().Be(3);
        alice.ManaPool.Generic.Should().Be(0, "the plot cost was paid");
    }

    [Fact]
    public void Plot_Fails_WhenPlotCostUnpaid_LeavesCardInHand()
    {
        var alice = new Player("Alice", 20);
        var card = InHand(alice);
        var reg = new PlotRegistry();

        var plotted = reg.Plot(card, alice, currentTurn: 1,
            payPlotCost: () => false);

        plotted.Should().BeFalse();
        card.Zone.Should().Be(ZoneType.Hand, "an unpaid plot leaves the card in hand");
        reg.IsPlotted(card).Should().BeFalse();
    }

    [Fact]
    public void CannotCast_OnTheSameTurnItWasPlotted()
    {
        var alice = new Player("Alice", 20);
        var card = InHand(alice);
        var reg = new PlotRegistry();

        reg.Plot(card, alice, currentTurn: 5, payPlotCost: () => true);

        reg.CanCastPlotted(card, currentTurn: 5).Should().BeFalse(
            "CR 718.2 — a plotted card can't be cast the turn it was plotted");
    }

    [Fact]
    public void CanCast_OnALaterTurn_ForFree()
    {
        var alice = new Player("Alice", 20);
        var card = InHand(alice);
        var reg = new PlotRegistry();

        reg.Plot(card, alice, currentTurn: 5, payPlotCost: () => true);

        reg.CanCastPlotted(card, currentTurn: 6).Should().BeTrue(
            "CR 718.2 — castable for free on a later turn");
    }

    [Fact]
    public void OncePerTurn_AfterCasting_CannotCastAgainSameTurn_ButCanNextTurn()
    {
        var alice = new Player("Alice", 20);
        var card = InHand(alice);
        var reg = new PlotRegistry();

        reg.Plot(card, alice, currentTurn: 1, payPlotCost: () => true);

        reg.CanCastPlotted(card, currentTurn: 2).Should().BeTrue();
        reg.MarkCastThisTurn(card, currentTurn: 2);
        reg.CanCastPlotted(card, currentTurn: 2).Should().BeFalse(
            "CR 718.2c — at most once per turn");

        // A new turn re-opens the once-per-turn allowance.
        reg.CanCastPlotted(card, currentTurn: 3).Should().BeTrue();
    }

    [Fact]
    public void Remove_StopsTracking()
    {
        var alice = new Player("Alice", 20);
        var card = InHand(alice);
        var reg = new PlotRegistry();

        reg.Plot(card, alice, currentTurn: 1, payPlotCost: () => true);
        reg.Remove(card);

        reg.IsPlotted(card).Should().BeFalse();
        reg.CanCastPlotted(card, currentTurn: 99).Should().BeFalse();
    }
}
