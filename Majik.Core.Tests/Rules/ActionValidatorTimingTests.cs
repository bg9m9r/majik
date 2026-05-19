using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Rules;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class ActionValidatorTimingTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Instant_AlwaysValid()
    {
        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        var action = new CastSpellAction(bolt, _alice, sorcerySpeedAvailable: false);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void VanillaCreature_OnOpponentTurn_IsInvalid()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var action = new CastSpellAction(bear, _alice, sorcerySpeedAvailable: false);
        var r = new ActionValidator().ValidateAction(action);
        r.IsValid.Should().BeFalse();
        r.ErrorMessage.Should().Contain("sorcery");
    }

    [Fact]
    public void CreatureWithFlash_OnOpponentTurn_IsValid()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        bear.AddAbility(new KeywordAbility("Flash"));
        var action = new CastSpellAction(bear, _alice, sorcerySpeedAvailable: false);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Creature_AtSorcerySpeed_IsValid()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var action = new CastSpellAction(bear, _alice, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }
}
