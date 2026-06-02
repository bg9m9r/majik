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
/// Unit tests for <see cref="BeholdTheMultiverseFactory"/>.
///
/// Behold the Multiverse (Kaldheim, {3}{U}, Instant):
///   "Scry 2, then draw two cards.
///    Foretell {1}{U} (...)"
///
/// Foretell (CR 702.143) is a documented v1 deferral — the cast pipeline does
/// not yet expose the foretell alternative cost, so the factory ships only the
/// printed {3}{U} mana-cost path (same posture as
/// <see cref="DoomskarFactory"/>). These tests cover the implemented surface:
/// the Instant shape and the "scry 2, then draw two" resolve body.
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with default scry (no agent registered) — both peeked cards
///     hit the bottom of the library, then TWO cards are drawn.
///   - Resolve when the controller's agent KEEPS BOTH peeked cards on top —
///     the two drawn cards are the originally top two, in order.
///   - Resolve on empty library — scry short-circuits and the draws flag the
///     player without throwing.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class BeholdTheMultiverseTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    [Fact]
    public void BeholdTheMultiverse_HasExpectedShape()
    {
        var card = BeholdTheMultiverseFactory.Create(_alice);

        card.Name.Should().Be("Behold the Multiverse");
        card.ManaCost.Should().Be("{3}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BeholdTheMultiverse()
    {
        var card = NamedCardFactory.Create("Behold the Multiverse", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Behold the Multiverse");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_DefaultScry_BottomsBoth_ThenDrawsTwo()
    {
        // Library: [a, b, c, d, e]. No agent registered → default sends both
        // peeked cards (`a`, `b`) to the bottom. New top = [c, d, e, a, b];
        // draw two pulls `c` and `d` into hand; library becomes [e, a, b].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");
        var e = SeedLibraryCard("E");

        var effect = BeholdTheMultiverseFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { c, d });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { e, a, b });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        c.Zone.Should().Be(ZoneType.Hand);
        d.Zone.Should().Be(ZoneType.Hand);
        a.Zone.Should().Be(ZoneType.Library);
        b.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Resolve_AgentKeepsBothOnTop_DrawsOriginalTopTwo()
    {
        // Library: [a, b, c, d]. ScriptedAgent keeps both peeked cards on top
        // in their original order. The trailing draw-two pulls `a` and `b`
        // into hand; library becomes [c, d].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a, b }));
        AgentRegistry.Set(_alice, agent);

        var effect = BeholdTheMultiverseFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a, b });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { c, d });
        a.Zone.Should().Be(ZoneType.Hand);
        b.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        var effect = BeholdTheMultiverseFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
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
