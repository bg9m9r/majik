using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="VisionsOfBeyondFactory"/>.
///
/// Visions of Beyond (Magic 2012, {U}, Instant):
///   "Draw a card. If a graveyard has twenty or more cards in it, draw
///    three cards instead."
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Threshold-met branch (controller's own yard ≥ 20) → draws 3.
///   - Threshold-met branch via OPPONENT's yard (CR 109.4 — any yard) → draws 3.
///   - Sub-threshold branch → draws 1.
///   - Null allPlayers (shape-only callers) falls back to caster's yard.
///   - MeetsThreshold predicate is independently observable.
/// </summary>
public class VisionsOfBeyondTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    [Fact]
    public void VisionsOfBeyond_HasExpectedShape()
    {
        var card = VisionsOfBeyondFactory.Create(_alice);

        card.Name.Should().Be("Visions of Beyond");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VisionsOfBeyond()
    {
        var card = NamedCardFactory.Create("Visions of Beyond", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Visions of Beyond");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_BelowThreshold_DrawsOne()
    {
        SeedGraveyard(_alice, 5);
        SeedGraveyard(_bob, 10);
        SeedLibrary(_alice, 5);

        var effect = VisionsOfBeyondFactory
            .BuildResolveEffect(_alice, new[] { _alice, _bob })
            .Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Library.GetCards().Should().HaveCount(4);
    }

    [Fact]
    public void Resolve_OwnGraveyardAtThreshold_DrawsThree()
    {
        SeedGraveyard(_alice, VisionsOfBeyondFactory.GraveyardThreshold);
        SeedLibrary(_alice, 5);

        var effect = VisionsOfBeyondFactory
            .BuildResolveEffect(_alice, new[] { _alice, _bob })
            .Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(VisionsOfBeyondFactory.BigDrawCount);
        _alice.Zones.Library.GetCards().Should().HaveCount(5 - VisionsOfBeyondFactory.BigDrawCount);
    }

    [Fact]
    public void Resolve_OpponentGraveyardAtThreshold_DrawsThree()
    {
        // CR 109.4 — "a graveyard" is any graveyard in the game; the
        // opponent's milled-out yard satisfies the gate even with the
        // caster's own yard empty.
        SeedGraveyard(_bob, VisionsOfBeyondFactory.GraveyardThreshold);
        SeedLibrary(_alice, 5);

        var effect = VisionsOfBeyondFactory
            .BuildResolveEffect(_alice, new[] { _alice, _bob })
            .Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(VisionsOfBeyondFactory.BigDrawCount);
    }

    [Fact]
    public void Resolve_NullAllPlayers_FallsBackToCasterGraveyard()
    {
        SeedGraveyard(_alice, VisionsOfBeyondFactory.GraveyardThreshold);
        SeedLibrary(_alice, 5);

        var effect = VisionsOfBeyondFactory.BuildResolveEffect(_alice, allPlayers: null).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(VisionsOfBeyondFactory.BigDrawCount);
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsDrawFromEmpty()
    {
        var effect = VisionsOfBeyondFactory
            .BuildResolveEffect(_alice, new[] { _alice, _bob })
            .Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void MeetsThreshold_ReturnsTrue_WhenAnyPlayerAtLimit()
    {
        SeedGraveyard(_bob, VisionsOfBeyondFactory.GraveyardThreshold);

        VisionsOfBeyondFactory
            .MeetsThreshold(_alice, new[] { _alice, _bob })
            .Should().BeTrue();
    }

    [Fact]
    public void MeetsThreshold_ReturnsFalse_WhenNobodyAtLimit()
    {
        SeedGraveyard(_alice, VisionsOfBeyondFactory.GraveyardThreshold - 1);
        SeedGraveyard(_bob, VisionsOfBeyondFactory.GraveyardThreshold - 1);

        VisionsOfBeyondFactory
            .MeetsThreshold(_alice, new[] { _alice, _bob })
            .Should().BeFalse();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void SeedGraveyard(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Yard{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Graveyard);
            p.Zones.Graveyard.AddCard(c);
        }
    }

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Lib{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }
}
