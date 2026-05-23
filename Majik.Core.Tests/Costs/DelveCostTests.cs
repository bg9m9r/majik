using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 702.66 — Delve. "For each generic mana in this spell's total cost,
/// you may exile a card from your graveyard rather than pay that mana."
///
/// Unit tests on <see cref="DelveCost"/> alone — cast-flow integration
/// is exercised by per-card tests (TreasureCruiseTests, DigThroughTimeTests).
/// </summary>
public class DelveCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    private Sorcery TreasureCruise()
    {
        // Printed cost {7}{U} — 7 generic, 1 blue. Delve targets generic.
        var c = new Sorcery("Treasure Cruise", "{7}{U}");
        c.SetOwner(_alice);
        return c;
    }

    private Card CardInGraveyard(string name, Player owner)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    [Fact]
    public void Constructor_NullSource_Throws()
    {
        var act = () => new DelveCost(null!, Array.Empty<ICard>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullChosen_Throws()
    {
        var act = () => new DelveCost(TreasureCruise(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReductionAmount_EqualsChosenCount()
    {
        var src = TreasureCruise();
        var a = CardInGraveyard("Brainstorm", _alice);
        var b = CardInGraveyard("Ponder", _alice);

        var cost = new DelveCost(src, new ICard[] { a, b });

        cost.ReductionAmount.Should().Be(2);
        cost.Chosen.Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void CanPay_AllInOwnGraveyard_True()
    {
        var src = TreasureCruise();
        var a = CardInGraveyard("Brainstorm", _alice);
        var b = CardInGraveyard("Ponder", _alice);
        var cost = new DelveCost(src, new ICard[] { a, b });

        cost.CanPay(_alice, ManaCost.Parse("{7}{U}")).Should().BeTrue();
    }

    [Fact]
    public void CanPay_CardNotInGraveyard_False()
    {
        var src = TreasureCruise();
        var a = CardInGraveyard("Brainstorm", _alice);
        // Move a out of the graveyard mid-test.
        _alice.Zones.Graveyard.RemoveCard(a);
        a.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(a);

        var cost = new DelveCost(src, new ICard[] { a });

        cost.CanPay(_alice, ManaCost.Parse("{7}{U}")).Should().BeFalse();
    }

    [Fact]
    public void CanPay_CardOwnedByOpponent_False()
    {
        var src = TreasureCruise();
        var foreign = CardInGraveyard("Bolt", _bob);

        var cost = new DelveCost(src, new ICard[] { foreign });

        cost.CanPay(_alice, ManaCost.Parse("{7}{U}")).Should().BeFalse();
    }

    [Fact]
    public void CanPay_DuplicateCard_False()
    {
        var src = TreasureCruise();
        var a = CardInGraveyard("Brainstorm", _alice);

        var cost = new DelveCost(src, new ICard[] { a, a });

        cost.CanPay(_alice, ManaCost.Parse("{7}{U}")).Should().BeFalse();
    }

    [Fact]
    public void CanPay_MoreThanGenericMana_False()
    {
        // Cost {1}{R} — only 1 generic. Choosing two cards is illegal: delve
        // can never reduce colored mana.
        var src = new Sorcery("Mini", "{1}{R}");
        src.SetOwner(_alice);
        var a = CardInGraveyard("Brainstorm", _alice);
        var b = CardInGraveyard("Ponder", _alice);

        var cost = new DelveCost(src, new ICard[] { a, b });

        cost.CanPay(_alice, ManaCost.Parse("{1}{R}")).Should().BeFalse();
    }

    [Fact]
    public void ApplyTo_ReducesGenericOnly_KeepsColored()
    {
        var src = TreasureCruise();
        var a = CardInGraveyard("Brainstorm", _alice);
        var b = CardInGraveyard("Ponder", _alice);
        var cost = new DelveCost(src, new ICard[] { a, b });

        var reduced = cost.ApplyTo(ManaCost.Parse("{7}{U}"));

        reduced.Generic.Should().Be(5);
        reduced.Blue.Should().Be(1);
        reduced.TotalValue.Should().Be(6);
    }

    [Fact]
    public void ApplyTo_ReductionExceedsGeneric_FloorsAtZero()
    {
        // CanPay would have refused this, but ApplyTo is robust to it.
        var src = TreasureCruise();
        var cards = Enumerable.Range(0, 10)
            .Select(i => CardInGraveyard($"Filler{i}", _alice))
            .Cast<ICard>().ToList();
        var cost = new DelveCost(src, cards);

        var reduced = cost.ApplyTo(ManaCost.Parse("{2}{U}"));

        reduced.Generic.Should().Be(0);
        reduced.Blue.Should().Be(1);
    }

    [Fact]
    public void Pay_ExilesAllChosenCardsFromGraveyard()
    {
        var src = TreasureCruise();
        var a = CardInGraveyard("Brainstorm", _alice);
        var b = CardInGraveyard("Ponder", _alice);
        var cost = new DelveCost(src, new ICard[] { a, b });

        cost.Pay(_alice);

        a.Zone.Should().Be(ZoneType.Exile);
        b.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(new[] { a, b });
        _alice.Zones.Exile.GetCards().Should().Contain(new[] { a, b });
    }

    [Fact]
    public void Pay_IllegalSelection_Throws()
    {
        var src = TreasureCruise();
        var foreign = CardInGraveyard("Bolt", _bob);
        var cost = new DelveCost(src, new ICard[] { foreign });

        var act = () => cost.Pay(_alice);

        act.Should().Throw<InvalidOperationException>();
    }
}
