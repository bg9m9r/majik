using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class OracleSpellBinderControlGlobalPumpTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GainControlOfCreature_RegistersControlChangeEffect()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Mind Control", ManaCost = "{3}{U}{U}",
              OracleText = "Gain control of target creature." },
            _alice, raw => raw, svc, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        svc.EffectiveController(bear).Should().BeSameAs(_alice);
    }

    [Fact]
    public void CreaturesYouControlGetPlusN_PumpsAllControlledCreatures()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        var elf = new Creature("Elf", "G", 1, 1)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        _alice.Zones.Battlefield.AddCard(bear);
        _alice.Zones.Battlefield.AddCard(elf);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Overrun", ManaCost = "{2}{G}{G}{G}",
              OracleText = "Creatures you control get +3/+3 until end of turn." },
            _alice, raw => raw, svc, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Power.Should().Be(5);
        elf.Power.Should().Be(4);

        svc.ExpireEndOfTurn();
        bear.Power.Should().Be(2);
    }
}
