using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

public class EnergyTokenTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GainEnergy_Accumulates()
    {
        _alice.GainEnergy(2);
        _alice.GainEnergy(3);
        _alice.EnergyCounters.Should().Be(5);
    }

    [Fact]
    public void PayEnergy_DeductsWhenAvailable()
    {
        _alice.GainEnergy(5);
        _alice.PayEnergy(3).Should().BeTrue();
        _alice.EnergyCounters.Should().Be(2);
    }

    [Fact]
    public void PayEnergy_RejectsAndPreservesBalanceWhenShort()
    {
        _alice.GainEnergy(2);
        _alice.PayEnergy(5).Should().BeFalse();
        _alice.EnergyCounters.Should().Be(2);
    }

    [Fact]
    public void Treasure_HasFiveColorManaOptions()
    {
        var treasure = TokenFactory.CreateTreasure(_alice);
        treasure.IsToken.Should().BeTrue();
        treasure.HasSubtype(CardSubtype.Treasure).Should().BeTrue();
        var produced = treasure.Abilities.OfType<Majik.Core.Abilities.IManaAbility>()
            .Select(a => a.ManaGenerated.ToString())
            .ToList();
        produced.Should().BeEquivalentTo(new[] { "W", "U", "B", "R", "G" });
    }

    [Fact]
    public void Clue_IsArtifactToken()
    {
        var clue = TokenFactory.CreateClue(_alice);
        clue.IsToken.Should().BeTrue();
        clue.HasSubtype(CardSubtype.Clue).Should().BeTrue();
    }
}
