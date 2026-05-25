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
/// Unit tests for <see cref="OptFactory"/>.
///
/// Opt (Invasion / Ixalan, {U}, Instant):
///   "Look at the top card of your library. You may put that card on the
///    bottom of your library. Draw a card." — effectively Scry 1 + draw 1.
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with default scry (no agent registered) — peeked card hits
///     the bottom of the library, a different card is drawn.
///   - Resolve when the controller's agent KEEPS the top card on the library
///     — the hand-drawn card is the same card that was on top, library
///     order on the top window is preserved.
///   - Resolve on empty library — scry short-circuits and the draw flags
///     the player without throwing.
///   - Single-card-library: peek + bottom + draw collapses to "draw the
///     single card"; no draw-from-empty flag fires.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class OptTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        // Tests register agents on the global AgentRegistry; clear so
        // cross-test ordering can't leak scry decisions into unrelated tests.
        AgentRegistry.Clear();
    }

    [Fact]
    public void Opt_HasExpectedShape()
    {
        var card = OptFactory.Create(_alice);

        card.Name.Should().Be("Opt");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Opt()
    {
        var card = NamedCardFactory.Create("Opt", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Opt");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Opt_Resolve_DefaultScry_BottomsTop_ThenDrawsNextCard()
    {
        // Library: [top, next, third]. No agent registered → default sends
        // the peeked card (`top`) to the bottom. New library = [next, third,
        // top]; the trailing draw pulls `next` into hand.
        var top = SeedLibraryCard("Top");
        var next = SeedLibraryCard("Next");
        var third = SeedLibraryCard("Third");

        var effect = OptFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { next });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { third, top });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        next.Zone.Should().Be(ZoneType.Hand);
        top.Zone.Should().Be(ZoneType.Library);
        third.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Opt_Resolve_AgentKeepsTop_DrawsTheSeenCard()
    {
        // Library: [top, next]. Register a ScriptedAgent that keeps `top` on
        // the library (TopOrder = [top], ToBottom = []). Then the draw
        // pulls `top` into hand; `next` stays on top of the library, now
        // empty above it.
        var top = SeedLibraryCard("Top");
        var next = SeedLibraryCard("Next");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { top }));
        AgentRegistry.Set(_alice, agent);

        var effect = OptFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { next });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        top.Zone.Should().Be(ZoneType.Hand);
        next.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Opt_Resolve_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        // No library cards. Scry short-circuits (peek empty); the draw
        // step flags the player for the draw-from-empty SBA but does not
        // throw. Hand and library remain empty.
        var effect = OptFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void Opt_Resolve_SingleCardLibrary_BottomsIt_ThenDrawsIt()
    {
        // Library has one card. Peek returns [a]; default scry sends it to
        // bottom. Library still [a] (single card; bottom == top). Then the
        // draw pulls `a` into hand — library now empty, no empty-draw flag.
        var a = SeedLibraryCard("A");

        var effect = OptFactory.BuildResolveEffect(_alice).Single();
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
