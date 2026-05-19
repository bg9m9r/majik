using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

public class EntersTappedReplacementTests
{
    [Fact]
    public void Land_WithEntersTappedReplacement_EntersTapped()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var alice = new Player("Alice", 20);
        var land = NamedCardFactory.Create("Mountain", alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        // Register: "if Mountain would enter the battlefield, it enters tapped"
        rep.Register<ZoneMoveIntent>(new LambdaReplacement<ZoneMoveIntent>(
            (i, _) => ReferenceEquals(i.Card, land) && i.ToZone == ZoneType.Battlefield,
            (i, _) => i with { EntersTapped = true }));

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeTrue();
        land.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Land_NoReplacement_EntersUntapped()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var alice = new Player("Alice", 20);
        var land = NamedCardFactory.Create("Forest", alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void ReplacementCancelled_CardDoesNotMove()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var alice = new Player("Alice", 20);
        var land = NamedCardFactory.Create("Mountain", alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        // "Cards can't enter the battlefield" — silly hypothetical, just tests cancellation.
        rep.Register<ZoneMoveIntent>(new LambdaReplacement<ZoneMoveIntent>(
            (i, _) => i.ToZone == ZoneType.Battlefield,
            (_, _) => null));

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        land.Zone.Should().Be(ZoneType.Hand);
    }
}
