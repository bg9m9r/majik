using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// Tests for CR 701.50 — Connive keyword action.
/// </summary>
public class ConniveActionTests
{
    [Fact]
    public void Apply_DrawsAndDiscards_AddsCounterForNonLand()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Controller = alice };
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        ConniveAction.Apply(bear);

        // Bear should have 1 +1/+1 counter (bolt was nonland).
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        alice.Zones.Library.GetCards().Should().NotContain(bolt);
        alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
    }

    [Fact]
    public void Apply_DiscardsLand_NoCounter()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Controller = alice };
        var land = new Land("Forest");
        land.SetOwner(alice);
        alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);

        ConniveAction.Apply(bear);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void ApplyN_RepeatsN_Times()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Controller = alice };
        var c1 = new Card("C1", ""); c1.SetOwner(alice);
        var c2 = new Card("C2", ""); c2.SetOwner(alice);
        var c3 = new Card("C3", ""); c3.SetOwner(alice);
        alice.Zones.Library.AddCard(c1);
        alice.Zones.Library.AddCard(c2);
        alice.Zones.Library.AddCard(c3);
        foreach (var c in new[] { c1, c2, c3 }) c.SetZone(ZoneType.Library);

        ConniveAction.ApplyN(bear, 3);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    [Fact]
    public void Apply_EmptyLibrary_NoDraw_NoOpGraceful()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Controller = alice };
        // Library is empty. Hand is empty. ConniveAction should no-op safely.
        Action act = () => ConniveAction.Apply(bear);
        act.Should().NotThrow();
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void Apply_NullTarget_Throws()
    {
        Action act = () => ConniveAction.Apply(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Apply_NoController_NoOp()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        // Controller is null — should return without throwing.
        Action act = () => ConniveAction.Apply(bear);
        act.Should().NotThrow();
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void ApplyN_ZeroOrNegative_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Controller = alice };
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        ConniveAction.ApplyN(bear, 0);
        ConniveAction.ApplyN(bear, -1);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        alice.Zones.Library.GetCards().Should().Contain(bolt);
    }
}
