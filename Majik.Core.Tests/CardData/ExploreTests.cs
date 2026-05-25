using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Explore (Worldwake / Modern Horizons 2, {1}{G}).
/// Sorcery — "You may play an additional land this turn. Draw a card."
///
/// Covers:
///   - Identity (name, type Sorcery, mana cost, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Resolve bumps the supplied <see cref="LandDropTracker"/>'s
///     per-turn max by 1 for the caster (CR 305.2).
///   - Resolve draws one card from the top of the library (CR 121.1).
///   - Resolve with a null tracker skips the bump but still draws (shape
///     path / unit-test escape hatch).
///   - Two Explores stack additively: the second reads the post-first
///     max and writes +1, giving max = 3 from the default 1.
///   - Empty library on the draw stamps the
///     <see cref="Player.TriedToDrawFromEmptyLibrary"/> loss flag
///     (CR 704.5b / CR 120.3).
/// </summary>
public class ExploreTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Explore_Identity()
    {
        var c = ExploreFactory.Create(_alice);

        c.Name.Should().Be("Explore");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Explore_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Explore", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Explore");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void Resolve_BumpsMaxLandDrops_AndDrawsACard()
    {
        var tracker = new LandDropTracker();
        // Default max = 1.
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(1);

        // Library: a single card to draw.
        var bolt = new Sorcery("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var effects = ExploreFactory.BuildResolveEffect(_alice, tracker);
        foreach (var e in effects) e.Execute();

        // CR 305.2 — extra-land bump.
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(2);

        // CR 121.1 — drew one card.
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NullTracker_SkipsBump_StillDraws()
    {
        var top = new Sorcery("Ponder", "{U}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var effects = ExploreFactory.BuildResolveEffect(_alice, landDropTracker: null);
        foreach (var e in effects) e.Execute();

        // Draw still applied; bump silently skipped (no tracker to mutate).
        _alice.Zones.Hand.GetCards().Should().Contain(top);
    }

    [Fact]
    public void Explore_TwoCopiesStackAdditively()
    {
        // Cast 1: max 1 → 2. Cast 2 (later in same turn): max 2 → 3.
        var tracker = new LandDropTracker();

        // Two cards to draw.
        var c1 = new Sorcery("Card 1", "{0}");
        var c2 = new Sorcery("Card 2", "{0}");
        c1.SetOwner(_alice); c2.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c1); c1.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c2); c2.SetZone(ZoneType.Library);

        var effects1 = ExploreFactory.BuildResolveEffect(_alice, tracker);
        foreach (var e in effects1) e.Execute();
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(2);

        var effects2 = ExploreFactory.BuildResolveEffect(_alice, tracker);
        foreach (var e in effects2) e.Execute();
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(3);

        // Both drew.
        _alice.Zones.Hand.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_TurnReset_DropsTheBump()
    {
        // CR 500.1 — turn change resets the per-turn max via
        // LandDropTracker.ResetTurn (called by TurnDriver on every turn
        // change). The bump is per-turn, not persistent.
        var tracker = new LandDropTracker();

        var top = new Sorcery("Card", "{0}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var effects = ExploreFactory.BuildResolveEffect(_alice, tracker);
        foreach (var e in effects) e.Execute();
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(2);

        // Simulate turn change.
        tracker.ResetTurn();
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(1);
    }

    [Fact]
    public void Resolve_EmptyLibrary_DrawStampsLossFlag()
    {
        // CR 704.5b — drawing from empty library doesn't throw; it stamps
        // the pending-loss sentinel via Fx.DrawCards' MarkTriedToDrawFromEmptyLibrary.
        var tracker = new LandDropTracker();

        var effects = ExploreFactory.BuildResolveEffect(_alice, tracker);
        var resolve = () => { foreach (var e in effects) e.Execute(); };

        resolve.Should().NotThrow();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
        // Bump still happened — the two halves of the spell are independent.
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(2);
    }
}
