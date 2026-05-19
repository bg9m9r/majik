using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

public class FlashbackAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CanCastFor_CardInGraveyard_OwnedBySelf_Yes()
    {
        var c = new Instant("Firebolt", "R") { Owner = _alice, Zone = ZoneType.Graveyard };
        var fb = new FlashbackAlternativeCost(ManaCost.Parse("4R"));

        fb.CanCastFor(c, _alice).Should().BeTrue();
    }

    [Fact]
    public void CanCastFor_NotInGraveyard_No()
    {
        var c = new Instant("Firebolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        var fb = new FlashbackAlternativeCost(ManaCost.Parse("4R"));

        fb.CanCastFor(c, _alice).Should().BeFalse();
    }

    [Fact]
    public void OnResolved_ExilesCardFromGraveyard()
    {
        var c = new Instant("Firebolt", "R") { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(c);
        var fb = new FlashbackAlternativeCost(ManaCost.Parse("4R"));

        fb.OnResolved(c, _alice);

        c.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c);
        _alice.Zones.Exile.GetCards().Should().Contain(c);
    }
}
