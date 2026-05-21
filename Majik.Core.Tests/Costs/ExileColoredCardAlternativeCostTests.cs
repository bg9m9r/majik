using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for ExileColoredCardAlternativeCost — the pitch mechanism used
/// by Force of Vigor and future Force-cycle spells (Force of Will, etc.).
/// CR 117.11 (alternative costs) + CR 701.21 (exile).
/// </summary>
public class ExileColoredCardAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── CanCastFor ────────────────────────────────────────────────────────────

    [Fact]
    public void CanCastFor_GreenCardInHand_OwnedBySelf_ReturnsTrue()
    {
        var bear = GreenCreatureInHand(_alice);
        var vigor = ForceInHand(_alice);

        var cost = new ExileColoredCardAlternativeCost(ManaColor.Green, bear);
        cost.CanCastFor(vigor, _alice).Should().BeTrue();
    }

    [Fact]
    public void CanCastFor_NonGreenCard_ReturnsFalse()
    {
        // A white card cannot pay the green pitch cost.
        var plains = new Card("Serra Angel", "{3}{W}{W}") { Owner = _alice };
        plains.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(plains);
        var vigor = ForceInHand(_alice);

        var cost = new ExileColoredCardAlternativeCost(ManaColor.Green, plains);
        cost.CanCastFor(vigor, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_GreenCardInGraveyard_ReturnsFalse()
    {
        // Card must be in hand at cast time (not graveyard).
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);
        var vigor = ForceInHand(_alice);

        var cost = new ExileColoredCardAlternativeCost(ManaColor.Green, bear);
        cost.CanCastFor(vigor, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_CardOwnedByOpponent_ReturnsFalse()
    {
        // Must be the caster's own card.
        var bear = GreenCreatureInHand(_bob);
        var vigor = ForceInHand(_alice);

        var cost = new ExileColoredCardAlternativeCost(ManaColor.Green, bear);
        cost.CanCastFor(vigor, _alice).Should().BeFalse();
    }

    // ── AlternativeManaCost ───────────────────────────────────────────────────

    [Fact]
    public void AlternativeManaCost_IsZero()
    {
        var bear = GreenCreatureInHand(_alice);
        var cost = new ExileColoredCardAlternativeCost(ManaColor.Green, bear);

        cost.AlternativeManaCost.Should().Be(ManaCost.Zero);
    }

    // ── Description ───────────────────────────────────────────────────────────

    [Fact]
    public void Description_ContainsColorName()
    {
        var bear = GreenCreatureInHand(_alice);
        var cost = new ExileColoredCardAlternativeCost(ManaColor.Green, bear);

        cost.Description.Should().Contain("Green");
    }

    // ── OnResolved ────────────────────────────────────────────────────────────

    [Fact]
    public void OnResolved_MovesExiledCardFromHandToExile()
    {
        var bear = GreenCreatureInHand(_alice);
        var vigor = ForceInHand(_alice);

        var cost = new ExileColoredCardAlternativeCost(ManaColor.Green, bear);
        // Confirm precondition.
        cost.CanCastFor(vigor, _alice).Should().BeTrue();

        cost.OnResolved(vigor, _alice);

        bear.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Hand.GetCards().Should().NotContain(bear);
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void OnResolved_CardAlreadyGone_DoesNotThrow()
    {
        // If the card was somehow removed before resolution, OnResolved is
        // still safe — it should not throw on a missing-from-hand card.
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Exile); // already moved
        var vigor = ForceInHand(_alice);

        var cost = new ExileColoredCardAlternativeCost(ManaColor.Green, bear);
        var act = () => cost.OnResolved(vigor, _alice);
        act.Should().NotThrow();
    }

    // ── Blue pitch (forward-compatibility for Force of Will) ─────────────────

    [Fact]
    public void CanCastFor_BlueCardInHand_BlueCost_ReturnsTrue()
    {
        var brainstorm = new Instant("Brainstorm", "{U}") { Owner = _alice };
        brainstorm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(brainstorm);
        var forceOfWill = new Instant("Force of Will", "{3}{U}{U}") { Owner = _alice };
        forceOfWill.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forceOfWill);

        var cost = new ExileColoredCardAlternativeCost(ManaColor.Blue, brainstorm);
        cost.CanCastFor(forceOfWill, _alice).Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature GreenCreatureInHand(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = owner };
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }

    private Instant ForceInHand(Player owner)
    {
        var c = new Instant("Force of Vigor", "{2}{G}{G}") { Owner = owner };
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }
}
