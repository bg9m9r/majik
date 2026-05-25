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
/// Unit tests for <see cref="ConsiderFactory"/>.
///
/// Consider (Innistrad: Midnight Hunt, {U}, Instant):
///   "Look at the top card of your library. You may put that card into your
///    graveyard. Then draw a card." — effectively Surveil 1 + draw 1.
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with default surveil (no agent registered) — peeked card hits
///     graveyard, a different library card is drawn.
///   - Resolve when the controller's agent KEEPS the top card on the library —
///     the hand-drawn card is the same card that was on top, graveyard untouched.
///   - Resolve on empty library — surveil short-circuits and the draw flags
///     the player without throwing.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class ConsiderTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        // Tests register agents on the global AgentRegistry; clear so cross-
        // test ordering can't leak surveil decisions into unrelated tests.
        AgentRegistry.Clear();
    }

    [Fact]
    public void Consider_HasExpectedShape()
    {
        var card = ConsiderFactory.Create(_alice);

        card.Name.Should().Be("Consider");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Consider()
    {
        var card = NamedCardFactory.Create("Consider", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Consider");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Consider_Resolve_DefaultSurveil_MillsTop_ThenDrawsNextCard()
    {
        // Library: [top, next, third]. With no agent registered the default
        // surveil decision sends the peeked card (`top`) to the graveyard,
        // then "draw a card" pulls the new top (`next`) into hand.
        var top = SeedLibraryCard("Top");
        var next = SeedLibraryCard("Next");
        var third = SeedLibraryCard("Third");

        var effect = ConsiderFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { next });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { third });
        top.Zone.Should().Be(ZoneType.Graveyard);
        next.Zone.Should().Be(ZoneType.Hand);
        third.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Consider_Resolve_AgentKeepsTop_DrawsTheSeenCard()
    {
        // Library: [top, next]. Register a ScriptedAgent that keeps `top` on
        // the library (TopOrder = [top], ToGraveyard = []). Then the draw
        // pulls `top` into hand; `next` stays on top.
        var top = SeedLibraryCard("Top");
        var next = SeedLibraryCard("Next");

        var agent = new ScriptedAgent();
        agent.QueueSurveilDecision(new SurveilAction.SurveilDecision(
            ToGraveyard: Array.Empty<ICard>(),
            TopOrder: new[] { (ICard)top }));
        AgentRegistry.Set(_alice, agent);

        var effect = ConsiderFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { next });
        top.Zone.Should().Be(ZoneType.Hand);
        next.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Consider_Resolve_EmptyLibrary_GracefulNoOp_FlagsDrawFromEmpty()
    {
        // No library cards. Surveil short-circuits (peek empty); the draw
        // step flags the player for the draw-from-empty SBA but does not
        // throw. Hand and graveyard remain empty.
        var effect = ConsiderFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
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
