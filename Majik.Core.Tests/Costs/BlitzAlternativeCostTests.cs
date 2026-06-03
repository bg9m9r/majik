using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for <see cref="BlitzAlternativeCost"/> (CR 702.152).
///
/// Blitz is an alternative cost (CR 702.152a) that — for Tenacious Underdog —
/// may be paid from the graveyard ("You may cast this card from your graveyard
/// using its blitz ability"). The cost itself only owns the zone/owner gate
/// plus the <see cref="Creature.BlitzWasPaid"/> stamp the three blitz riders
/// read; the "Pay 2 life" portion is a separate <see cref="IAdditionalCost"/>
/// fed alongside it through <see cref="Majik.Core.Game.SpellCastFlow"/>.
/// Mirror of <see cref="EvokeAlternativeCostTests"/>.
/// </summary>
public class BlitzAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CanCastFor_CardInGraveyard_OwnedBySelf_True()
    {
        var underdog = MakeCreatureInGraveyard(_alice, "Tenacious Underdog", "{1}{B}", 3, 2);
        var cost = BlitzAlternativeCost.FromGraveyard(ManaCost.Parse("2BB"));

        cost.CanCastFor(underdog, _alice).Should().BeTrue();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("2BB"));
    }

    [Fact]
    public void CanCastFor_CardInHand_FalseForGraveyardBlitz()
    {
        // Tenacious Underdog's blitz is graveyard-only; a hand-resident copy
        // can't use the graveyard blitz gate.
        var underdog = MakeCreatureInGraveyard(_alice, "Tenacious Underdog", "{1}{B}", 3, 2);
        _alice.Zones.Graveyard.RemoveCard(underdog);
        _alice.Zones.Hand.AddCard(underdog);
        underdog.SetZone(ZoneType.Hand);

        var cost = BlitzAlternativeCost.FromGraveyard(ManaCost.Parse("2BB"));
        cost.CanCastFor(underdog, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_CardOwnedByOpponent_False()
    {
        var underdog = MakeCreatureInGraveyard(_bob, "Tenacious Underdog", "{1}{B}", 3, 2);
        var cost = BlitzAlternativeCost.FromGraveyard(ManaCost.Parse("2BB"));

        cost.CanCastFor(underdog, _alice).Should().BeFalse();
    }

    [Fact]
    public void OnResolved_SetsBlitzWasPaidOnCreature()
    {
        var underdog = MakeCreatureInGraveyard(_alice, "Tenacious Underdog", "{1}{B}", 3, 2);
        var cost = BlitzAlternativeCost.FromGraveyard(ManaCost.Parse("2BB"));

        cost.OnResolved(underdog, _alice);

        underdog.BlitzWasPaid.Should().BeTrue();
    }

    [Fact]
    public void Description_MentionsBlitzAndCost()
    {
        var cost = BlitzAlternativeCost.FromGraveyard(ManaCost.Parse("2BB"));
        cost.Description.Should().Contain("Blitz");
        cost.Description.Should().Contain("2BB");
    }

    [Fact]
    public void FromHand_CanCastFor_CardInHand_True()
    {
        // The generic blitz cluster (non-Tenacious-Underdog) is cast from hand.
        var hasty = MakeCreatureInGraveyard(_alice, "Generic Blitzer", "{1}{R}", 2, 2);
        _alice.Zones.Graveyard.RemoveCard(hasty);
        _alice.Zones.Hand.AddCard(hasty);
        hasty.SetZone(ZoneType.Hand);

        var cost = BlitzAlternativeCost.FromHand(ManaCost.Parse("R"));
        cost.CanCastFor(hasty, _alice).Should().BeTrue();
        cost.OnResolved(hasty, _alice);
        hasty.BlitzWasPaid.Should().BeTrue();
    }

    private static Creature MakeCreatureInGraveyard(
        Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness) { Owner = owner };
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }
}
