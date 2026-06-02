using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GrowthSpiralFactory"/> (Ravnica Allegiance,
/// {G}{U}).
///
/// Instant — "Draw a card. You may put a land card from your hand onto the
/// battlefield."
///
/// Covers:
/// - Identity ({G}{U} Instant, green + blue, mana value 2).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve draws one card from the top of the library (CR 121.1).
/// - Resolve then puts the first land card from hand onto the battlefield
///   (CR 305.9 / 113.6c — NOT a land drop, no land-drop tracker touched).
/// - Resolve with no land in hand draws but is a clean no-op for the
///   land-play half ("you may" with no candidate).
/// - Resolve on an empty library stamps the CR 704.5b pending-loss flag and
///   does not throw; the land-play half still runs.
/// - The drawn card itself becomes a candidate for the land-play half (the
///   draw happens first, exactly as printed).
/// </summary>
[Trait("Color", "M")]
public class GrowthSpiralFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GrowthSpiral_Identity()
    {
        var c = GrowthSpiralFactory.Create(_alice);

        c.Name.Should().Be("Growth Spiral");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.ManaCost.Should().Be("{G}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GrowthSpiral_IsGreenAndBlue()
    {
        var c = GrowthSpiralFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Growth Spiral has a {G} pip");
        colors.Should().Contain(ManaColor.Blue, "Growth Spiral has a {U} pip");
        colors.Should().HaveCount(2);
    }

    [Fact]
    public void GrowthSpiral_ManaValue_IsTwo()
    {
        var c = GrowthSpiralFactory.Create(_alice);

        // {G}{U} = mana value 2 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {G}{U} has mana value 2");
    }
    // -----------------------------------------------------------------------
    // Resolve — draw a card, then put a land from hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsACard_AndPutsLandFromHandOntoBattlefield()
    {
        // Top of library: a nonland to draw.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        // Hand: a land available to put onto the battlefield.
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var effects = GrowthSpiralFactory.BuildResolveEffect(_alice, zoneService: null);
        foreach (var e in effects) e.Execute();

        // CR 121.1 — drew the top card into hand.
        _alice.Zones.Hand.GetCards().Should().Contain(bolt, "Growth Spiral draws a card");
        _alice.Zones.Library.GetCards().Should().BeEmpty("the single library card was drawn");

        // CR 305.9 / 113.6c — the land from hand is now on the battlefield.
        forest.Zone.Should().Be(ZoneType.Battlefield,
            "the land from hand is put onto the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.Controller.Should().BeSameAs(_alice,
            "the land enters under its owner's control (CR 110.2a)");
    }

    [Fact]
    public void Resolve_NoLandInHand_DrawsButLandPlayIsNoOp()
    {
        var top = new Instant("Opt", "{U}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Hand has only a nonland — no land candidate for the "you may" clause.
        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(counterspell);
        counterspell.SetZone(ZoneType.Hand);

        var effects = GrowthSpiralFactory.BuildResolveEffect(_alice, zoneService: null);
        var resolve = () => { foreach (var e in effects) e.Execute(); };

        resolve.Should().NotThrow("no land in hand → the optional land-play is a clean no-op");
        _alice.Zones.Hand.GetCards().Should().Contain(top, "the draw still happened");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty("no land was put onto the battlefield");
    }

    [Fact]
    public void Resolve_EmptyLibrary_StampsLossFlag_LandPlayStillRuns()
    {
        // CR 704.5b — drawing from an empty library does not throw; it stamps
        // the pending-loss sentinel. The land-play half is independent and
        // still runs.
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var effects = GrowthSpiralFactory.BuildResolveEffect(_alice, zoneService: null);
        var resolve = () => { foreach (var e in effects) e.Execute(); };

        resolve.Should().NotThrow();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library stamps the CR 704.5b loss flag");
        forest.Zone.Should().Be(ZoneType.Battlefield,
            "the two halves of the spell are independent — the land still enters");
    }

    [Fact]
    public void Resolve_DrawnLandBecomesPlayCandidate()
    {
        // The draw happens BEFORE the land-play (as printed), so a land drawn
        // by Growth Spiral is a legal candidate for its own land-play clause.
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);
        // Hand starts empty.

        var effects = GrowthSpiralFactory.BuildResolveEffect(_alice, zoneService: null);
        foreach (var e in effects) e.Execute();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            "the drawn land is a candidate for the same spell's land-play clause");
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
