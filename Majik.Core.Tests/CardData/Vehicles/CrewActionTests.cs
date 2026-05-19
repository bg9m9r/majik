using FluentAssertions;
using Majik.Core.CardData.Vehicles;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class CrewActionTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Crew_SufficientPower_TapsAndAppliesEffect()
    {
        var effects = new ContinuousEffectsService();
        var vehicle = new Creature("Skysovereign", "6", 0, 0)
        {
            Owner = _alice, Controller = _alice, ActiveEffects = effects,
            HasSummoningSickness = false,
        };
        var crew1 = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, HasSummoningSickness = false };
        var crew2 = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, HasSummoningSickness = false };

        var result = CrewAction.Crew(vehicle, crewCost: 3,
            vehiclePower: 6, vehicleToughness: 5,
            new[] { crew1, crew2 }, effects);

        result.Success.Should().BeTrue();
        crew1.IsTapped.Should().BeTrue();
        crew2.IsTapped.Should().BeTrue();
        vehicle.Power.Should().Be(6);
        vehicle.Toughness.Should().Be(5);
    }

    [Fact]
    public void Crew_InsufficientPower_Fails_NoTap()
    {
        var effects = new ContinuousEffectsService();
        var vehicle = new Creature("Big Ship", "6", 0, 0)
        { Owner = _alice, Controller = _alice, ActiveEffects = effects };
        var crew1 = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, HasSummoningSickness = false };

        var result = CrewAction.Crew(vehicle, crewCost: 5, 6, 5,
            new[] { crew1 }, effects);

        result.Success.Should().BeFalse();
        crew1.IsTapped.Should().BeFalse();
        vehicle.Power.Should().Be(0);
    }

    [Fact]
    public void Crew_SummoningSickCrewmate_Rejected()
    {
        var effects = new ContinuousEffectsService();
        var vehicle = new Creature("Ship", "6", 0, 0)
        { Owner = _alice, Controller = _alice, ActiveEffects = effects };
        var sick = new Creature("Bear", "1G", 5, 5)
        { Owner = _alice, Controller = _alice, HasSummoningSickness = true };

        var result = CrewAction.Crew(vehicle, 3, 6, 5, new[] { sick }, effects);

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("summoning-sick");
    }

    [Fact]
    public void Crew_ExpiresAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var vehicle = new Creature("Ship", "6", 0, 0)
        { Owner = _alice, Controller = _alice, ActiveEffects = effects, HasSummoningSickness = false };
        var crew = new Creature("Bear", "1G", 3, 3)
        { Owner = _alice, Controller = _alice, HasSummoningSickness = false };

        CrewAction.Crew(vehicle, 3, 6, 5, new[] { crew }, effects);
        vehicle.Power.Should().Be(6);

        effects.ExpireEndOfTurn();
        vehicle.Power.Should().Be(0);
    }
}
