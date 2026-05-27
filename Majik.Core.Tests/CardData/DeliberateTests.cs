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
/// Unit tests for <see cref="DeliberateFactory"/>.
///
/// Deliberate (Zendikar Rising, {1}{U}, Instant):
///   "Scry 2, then draw a card."
///
/// Covers:
///   - Card identity (name, instant type, mana cost {1}{U}, mana value 2, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with default scry (no agent registered) — both peeked cards
///     hit the bottom of the library, a different card is drawn.
///   - Resolve when the controller's agent KEEPS BOTH peeked cards on top —
///     the hand-drawn card is the originally top card, library order is
///     preserved on the upper window.
///   - Resolve on empty library — scry short-circuits and the draw flags
///     the player without throwing.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class DeliberateTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    [Fact]
    public void Deliberate_HasExpectedShape()
    {
        var card = DeliberateFactory.Create(_alice);

        card.Name.Should().Be("Deliberate");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Deliberate_HasManaValueTwo()
    {
        var card = DeliberateFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{U} = mana value 2");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Deliberate()
    {
        var card = NamedCardFactory.Create("Deliberate", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Deliberate");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Deliberate_Resolve_DefaultScry_BottomsBoth_ThenDraws()
    {
        // Library: [a, b, c, d]. No agent registered → default sends both
        // peeked cards (`a`, `b`) to the bottom. New top = `c`, drawn into
        // hand; library becomes [d, a, b].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var effect = DeliberateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { c });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { d, a, b });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        c.Zone.Should().Be(ZoneType.Hand);
        a.Zone.Should().Be(ZoneType.Library);
        b.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Deliberate_Resolve_AgentKeepsBothOnTop_DrawsOriginalTop()
    {
        // Library: [a, b, c]. ScriptedAgent keeps both peeked cards on top
        // in their original order. The trailing draw pulls `a` into hand;
        // library becomes [b, c].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a, b }));
        AgentRegistry.Set(_alice, agent);

        var effect = DeliberateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c });
        a.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Deliberate_Resolve_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        var effect = DeliberateFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void Deliberate_Resolve_SingleCardLibrary_BottomsIt_ThenDraws()
    {
        // Library has one card. Peek returns [a]; default scry sends it to
        // bottom. Library still [a] (single card; bottom == top). Then the
        // draw pulls `a` into hand — library now empty.
        var a = SeedLibraryCard("A");

        var effect = DeliberateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
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
