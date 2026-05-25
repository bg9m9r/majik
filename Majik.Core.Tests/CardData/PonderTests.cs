using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PonderFactory"/>.
///
/// Ponder (Lorwyn / Modern Horizons 3, {U}, Sorcery):
///   "Look at the top three cards of your library, then put them back in any
///    order. You may shuffle your library. Draw a card."
///
/// Covers:
///   - Card identity (name, sorcery type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with default reorder (no agent registered) — peeked order
///     preserved, top card pulled into hand by the trailing draw.
///   - Resolve when the controller's agent REVERSES the top three — the
///     hand-drawn card is the previous bottom of the peeked window.
///   - Resolve on empty library — peek short-circuits and the draw flags
///     the player without throwing.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class PonderTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        // Tests register agents on the global AgentRegistry; clear so cross-
        // test ordering can't leak scry decisions into unrelated tests.
        AgentRegistry.Clear();
    }

    [Fact]
    public void Ponder_HasExpectedShape()
    {
        var card = PonderFactory.Create(_alice);

        card.Name.Should().Be("Ponder");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Ponder()
    {
        var card = NamedCardFactory.Create("Ponder", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Ponder");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ponder_Resolve_DefaultReorder_DrawsOriginalTop()
    {
        // Library: [a, b, c, d]. No agent registered → default keeps the
        // peeked window [a, b, c] in original order on top; the draw then
        // pulls `a` into hand.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var effect = PonderFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c, d });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        a.Zone.Should().Be(ZoneType.Hand);
        b.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Ponder_Resolve_AgentReversesTop_DrawsPreviousBottomOfWindow()
    {
        // Library: [a, b, c, d]. ScriptedAgent reverses the top window to
        // [c, b, a]; ToBottom = [] (Ponder is reorder-only). The trailing
        // draw then pulls `c` into hand; library becomes [b, a, d].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { c, b, a }));
        // Ponder's "may shuffle" rider now consults ChooseYesNoAsync
        // (CR 701.20 + BotIntent.LibraryReorder). Decline so the reorder
        // assertion below still observes the post-reorder top.
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var effect = PonderFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { c });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, a, d });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        c.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Ponder_Resolve_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        // No library cards. Peek short-circuits; the draw step flags the
        // player for the draw-from-empty SBA but does not throw.
        var effect = PonderFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void Ponder_Resolve_ShortLibrary_PeekTakesWhatExists_AndDraws()
    {
        // Library has fewer than 3 cards. Peek returns [a, b], reorder
        // preserves order (default), then the draw pulls `a` into hand.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");

        var effect = PonderFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b });
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
