using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

public class MoreAltCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Madness_CanCastFromExile_OwnedBySelf()
    {
        var c = new Instant("Fiery Temper", "1BR") { Owner = _alice, Zone = ZoneType.Exile };
        var madness = new MadnessAlternativeCost(ManaCost.Parse("R"));

        madness.CanCastFor(c, _alice).Should().BeTrue();
        madness.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));
    }

    [Fact]
    public void Madness_NotInExile_No()
    {
        var c = new Instant("Fiery Temper", "1BR") { Owner = _alice, Zone = ZoneType.Hand };
        var madness = new MadnessAlternativeCost(ManaCost.Parse("R"));

        madness.CanCastFor(c, _alice).Should().BeFalse();
    }

    [Fact]
    public void Madness_OpponentOwnsCard_No()
    {
        var c = new Instant("Fiery Temper", "1BR") { Owner = _bob, Zone = ZoneType.Exile };
        var madness = new MadnessAlternativeCost(ManaCost.Parse("R"));

        madness.CanCastFor(c, _alice).Should().BeFalse();
    }

    [Fact]
    public void CastFromExile_DescriptionAndCostExposed()
    {
        var alt = new CastFromExileAlternativeCost("Suspend", ManaCost.Parse("R"));
        alt.Description.Should().Be("Suspend");
        alt.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));
    }

    [Fact]
    public void CastFromExile_GraveyardCard_No()
    {
        var alt = new CastFromExileAlternativeCost("Cascade", ManaCost.Parse("0"));
        var c = new Instant("X", "1") { Owner = _alice, Zone = ZoneType.Graveyard };
        alt.CanCastFor(c, _alice).Should().BeFalse();
    }
}
