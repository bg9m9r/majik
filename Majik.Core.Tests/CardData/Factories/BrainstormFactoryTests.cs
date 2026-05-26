using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Brainstorm (Ice Age and many reprints, {U}, Instant).
///
/// Oracle text:
///   "Draw three cards, then put two cards from your hand on top of your
///    library in any order."
///
/// Covers:
///   - Card identity (Instant, {U}, blue, owner/controller).
///   - NamedCardFactory dispatch by name.
///   - Draw three then put two on top — no-agent deterministic path.
///   - Empty-library mid-draw flags the loss SBA but still returns
///     remaining hand cards to the top of the library.
///   - Hand smaller than 2 after drawing returns however many exist.
/// </summary>
public class BrainstormFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Brainstorm_HasInstantShape_Blue_AtCostU()
    {
        var card = BrainstormFactory.Create(_alice);

        card.Name.Should().Be("Brainstorm");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBrainstormShape()
    {
        var dispatched = NamedCardFactory.Create("Brainstorm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Brainstorm");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void Brainstorm_Resolve_NoAgent_DrawsThree_ReturnsLastTwo()
    {
        // Hand starts with "InHand-A". Library = [L1, L2, L3, L4].
        // After drawing 3: hand = [InHand-A, L1, L2, L3], library = [L4].
        // Deterministic fallback picks the last-of-hand twice → returns
        // L3 then L2 to the top. Library is then [L2, L3, L4].
        // Note: InsertCardAt(0) is called twice; the SECOND insert lands
        // on top, so L2 sits above L3.
        var inHandA = NewCardInHand("InHand-A");
        var l1 = NewLibraryCardAtEnd("L1");
        var l2 = NewLibraryCardAtEnd("L2");
        var l3 = NewLibraryCardAtEnd("L3");
        var l4 = NewLibraryCardAtEnd("L4");

        var effect = BrainstormFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().Equal(new[] { "InHand-A", "L1" });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { l2, l3, l4 });
        _alice.Zones.Library.GetCards().Select(c => c.Name).Should().Equal(new[] { "L2", "L3", "L4" });
        // Zones updated consistently for the cards that moved.
        l1.Zone.Should().Be(ZoneType.Hand);
        l2.Zone.Should().Be(ZoneType.Library);
        l3.Zone.Should().Be(ZoneType.Library);
        inHandA.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Brainstorm_Resolve_EmptyLibrary_MarksLossFlag_NoMoves()
    {
        // Library empty + hand empty → first draw flags the loss SBA;
        // the return clause then has nothing to do.
        var effect = BrainstormFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            because: "the first draw attempt against an empty library flags CR 704.5b");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Brainstorm_Resolve_HandHasOneCardAfterDraw_ReturnsOnlyThatOne()
    {
        // Library has one card; hand starts empty. After draw loop:
        // hand = [L1], library = []. The return clause returns the only
        // available card (no underflow). Library ends [L1]; hand empty.
        var l1 = NewLibraryCardAtEnd("L1");

        var effect = BrainstormFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            because: "the single drawn card is returned to the top of the library");
        _alice.Zones.Library.GetCards().Should().Equal(new[] { l1 });
        l1.Zone.Should().Be(ZoneType.Library);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            because: "draws 2 and 3 hit an empty library");
    }

    [Fact]
    public void Brainstorm_Resolve_AgentPicks_ReturnsAgentPicks()
    {
        // Hand pre-existing: [Keep-A, Junk-B]. Library: [L1, L2, L3, L4].
        // After draws: hand = [Keep-A, Junk-B, L1, L2, L3], library = [L4].
        // Agent script: pick "Junk-B" first, then "L1" — the agent drives
        // both return picks. Library ends [L1, Junk-B, L4]: second insert
        // (L1) lands on top; Junk-B sits one below; L4 stays at the bottom.
        var keepA = NewCardInHand("Keep-A");
        var junkB = NewCardInHand("Junk-B");
        var l1 = NewLibraryCardAtEnd("L1");
        var l2 = NewLibraryCardAtEnd("L2");
        var l3 = NewLibraryCardAtEnd("L3");
        var l4 = NewLibraryCardAtEnd("L4");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(hand => hand.FirstOrDefault(c => c.Name == "Junk-B"));
        agent.QueueFromHand(hand => hand.FirstOrDefault(c => c.Name == "L1"));

        var effect = BrainstormFactory.BuildResolveEffect(_alice, agent).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().Equal(new[] { "Keep-A", "L2", "L3" });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { l1, junkB, l4 });
        junkB.Zone.Should().Be(ZoneType.Library);
        l1.Zone.Should().Be(ZoneType.Library);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ICard NewCardInHand(string name)
    {
        var c = new Instant(name, "{0}") { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(c);
        return c;
    }

    private ICard NewLibraryCardAtEnd(string name)
    {
        var c = new Instant(name, "{0}") { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c);
        return c;
    }

}
