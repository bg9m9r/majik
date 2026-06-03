using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

/// <summary>
/// CR 118.9 / CR 116.3a — Bolas's Citadel's "pay life equal to its mana value
/// rather than pay its mana cost" alternative cost.
/// </summary>
public class PayLifeEqualToManaValueAlternativeCostTests
{
    private static Sorcery Spell(string cost) =>
        new("Test Spell", cost) { Owner = new Player("Owner", 20) };

    [Fact]
    public void AlternativeManaCost_IsZero_NoManaPaid()
    {
        var alt = new PayLifeEqualToManaValueAlternativeCost();
        alt.AlternativeManaCost.Should().Be(ManaCost.Zero);
    }

    [Theory]
    [InlineData("{3}{B}{B}{B}", 6)]
    [InlineData("{R}", 1)]
    [InlineData("{0}", 0)]
    [InlineData("{2}{G}", 3)]
    public void LifeAmountFor_EqualsManaValue(string cost, int expected)
    {
        PayLifeEqualToManaValueAlternativeCost.LifeAmountFor(Spell(cost))
            .Should().Be(expected);
    }

    [Fact]
    public void CanCastFor_True_WhenLifeAtLeastManaValue()
    {
        var alt = new PayLifeEqualToManaValueAlternativeCost();
        var caster = new Player("Alice", 6);
        alt.CanCastFor(Spell("{3}{B}{B}{B}"), caster).Should().BeTrue();
    }

    [Fact]
    public void CanCastFor_False_WhenLifeBelowManaValue()
    {
        // CR 119.4 — you can't pay life you don't have.
        var alt = new PayLifeEqualToManaValueAlternativeCost();
        var caster = new Player("Alice", 5);
        alt.CanCastFor(Spell("{3}{B}{B}{B}"), caster).Should().BeFalse();
    }

    [Fact]
    public void OnResolved_PaysLifeEqualToManaValue()
    {
        // CR 118.8 — the life is paid as the cost is paid; routed through
        // LoseLife so life-loss triggers fire.
        var alt = new PayLifeEqualToManaValueAlternativeCost();
        var caster = new Player("Alice", 20);

        alt.OnResolved(Spell("{3}{B}{B}{B}"), caster);

        caster.LifeTotal.Should().Be(14);
        caster.LifeLostThisTurn.Should().Be(6);
    }

    [Fact]
    public void OnResolved_ZeroManaValue_NoLifeLost()
    {
        var alt = new PayLifeEqualToManaValueAlternativeCost();
        var caster = new Player("Alice", 20);

        alt.OnResolved(Spell("{0}"), caster);

        caster.LifeTotal.Should().Be(20);
        caster.LifeLostThisTurn.Should().Be(0);
    }
}
