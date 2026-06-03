using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
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

    // -----------------------------------------------------------------------
    // Agent-driven discard pick (CR 701.50a — "that player discards a card";
    // the discarding player chooses which). Pays down the v1 "agent-driven
    // connive discard pick" residual.
    // -----------------------------------------------------------------------

    [Fact]
    public void Apply_AgentChoosesWhichCardToDiscard()
    {
        using var _ = AgentRegistry.PushScope();
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Controller = alice };

        // Two cards already in hand: a Land (Forest) and a nonland (Bolt). The
        // connive draw will be a third card on top of the library.
        var forest = new Land("Forest"); forest.SetOwner(alice);
        forest.SetZone(ZoneType.Hand); alice.Zones.Hand.AddCard(forest);
        var bolt = new Card("Lightning Bolt", "{R}"); bolt.SetOwner(alice);
        bolt.SetZone(ZoneType.Hand); alice.Zones.Hand.AddCard(bolt);

        var drawn = new Card("Drawn Spell", "{1}"); drawn.SetOwner(alice);
        drawn.SetZone(ZoneType.Library); alice.Zones.Library.AddCard(drawn);

        // Agent deliberately discards the LAND (forest) — not the just-drawn
        // card the deterministic v1 policy would have picked. A land discard
        // yields NO +1/+1 counter (CR 701.50a).
        var agent = new ScriptedAgent();
        agent.QueueFromHand(hand => hand.OfType<Land>().Cast<ICard>().First());
        AgentRegistry.Set(alice, agent);

        ConniveAction.Apply(bear);

        alice.Zones.Graveyard.GetCards().Should().Contain(forest,
            "the agent chose to discard the Forest");
        alice.Zones.Graveyard.GetCards().Should().NotContain(drawn,
            "the agent did NOT discard the just-drawn card");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "a LAND was discarded → no +1/+1 counter (CR 701.50a)");
    }

    [Fact]
    public void ApplyN_DrawsAllThenDiscardsAll_AgentSeesEveryDrawnCard()
    {
        using var _ = AgentRegistry.PushScope();
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Controller = alice };

        // Connive 2: draws 2 cards (a land + a nonland), THEN discards 2.
        // Per CR 701.50b the draws happen first as a batch, so the agent sees
        // BOTH drawn cards in hand before any discard.
        var land = new Land("Forest"); land.SetOwner(alice);
        land.SetZone(ZoneType.Library); alice.Zones.Library.AddCard(land);
        var spell = new Card("Spell", "{1}"); spell.SetOwner(alice);
        spell.SetZone(ZoneType.Library); alice.Zones.Library.AddCard(spell);

        var maxHandSeen = 0;
        var agent = new ScriptedAgent();
        agent.QueueFromHand(hand => { maxHandSeen = System.Math.Max(maxHandSeen, hand.Count); return hand[0]; });
        agent.QueueFromHand(hand => { maxHandSeen = System.Math.Max(maxHandSeen, hand.Count); return hand[0]; });
        AgentRegistry.Set(alice, agent);

        ConniveAction.ApplyN(bear, 2);

        maxHandSeen.Should().Be(2,
            "connive 2 draws both cards BEFORE discarding (CR 701.50b) — the agent's first discard prompt sees a 2-card hand");
        alice.Zones.Hand.GetCards().Should().BeEmpty("both drawn cards were discarded");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "exactly one of the two discarded cards was a nonland → one +1/+1 counter");
    }
}
