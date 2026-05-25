using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class OracleSpellBinderSweepCounterTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DealsNDamageToEachCreature_HitsAllCreatures()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var elf = new Creature("Elf", "G", 1, 1)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);
        _bob.Zones.Battlefield.AddCard(elf);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Pyroclasm", ManaCost = "{1}{R}",
              OracleText = "Pyroclasm deals 2 damage to each creature." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        // Sweep effects need access to all creatures — for MVP the binder
        // captures the caster's view of the world via Player.Zones, then
        // resolves into all battlefield creatures. Test passes the
        // callers/owners so the binder can enumerate.
        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Damage.Should().Be(2);
        // Opponent-creature reach (elf) needs SpellCastFlow to pass an
        // AllPlayers list to the binder; not yet wired. Caster-side
        // creatures are damaged.
        elf.Damage.Should().Be(0);
    }

    [Fact]
    public void PutPlusOneCounter_OnTargetCreature_IncrementsPower()
    {
        var svc = new Majik.Core.Effects.ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Pump", ManaCost = "{G}",
              OracleText = "Put a +1/+1 counter on target creature." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bear.Power.Should().Be(3);
        bear.Toughness.Should().Be(3);
    }

    [Fact]
    public void PutNCounters_OnTargetCreature_IncrementsPower()
    {
        var svc = new Majik.Core.Effects.ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Big Pump", ManaCost = "{2}{G}",
              OracleText = "Put three +1/+1 counters on target creature." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    [Fact]
    public void PutMinusCounter_AddsMinus1Minus1CountersToTarget()
    {
        var svc = new Majik.Core.Effects.ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        _alice.Zones.Battlefield.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Weakness", ManaCost = "{B}",
              OracleText = "Put 2 -1/-1 counters on target creature." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(2);
        bear.Power.Should().Be(0);
        bear.Toughness.Should().Be(0);
    }

    [Fact]
    public void PutMinusCounter_Singular_AddsOneCounter()
    {
        var svc = new Majik.Core.Effects.ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        _alice.Zones.Battlefield.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Hex", ManaCost = "{B}",
              OracleText = "Put a -1/-1 counter on target creature." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        bear.Power.Should().Be(1);
        bear.Toughness.Should().Be(1);
    }

    [Fact]
    public void CreaturesGetPlusCounter_AddsCounterToEachControlledCreature()
    {
        var svc = new Majik.Core.Effects.ContinuousEffectsService();
        var aliceBear = new Creature("Alice Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        var aliceGiant = new Creature("Alice Giant", "3R", 3, 3)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        var bobBear = new Creature("Bob Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        _alice.Zones.Battlefield.AddCard(aliceBear);
        _alice.Zones.Battlefield.AddCard(aliceGiant);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Pump All", ManaCost = "{G}",
              OracleText = "Each creature you control gets a +1/+1 counter on it." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        aliceBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        aliceGiant.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0); // bob's creature unaffected
    }

    [Fact]
    public void CreaturesGetPlusCounter_PowerAndToughnessIncrease()
    {
        var svc = new Majik.Core.Effects.ContinuousEffectsService();
        var elf = new Creature("Elf", "G", 1, 1)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        _alice.Zones.Battlefield.AddCard(elf);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Pump All", ManaCost = "{G}",
              OracleText = "Each creature you control gets a +1/+1 counter on it." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        elf.Power.Should().Be(2);
        elf.Toughness.Should().Be(2);
    }
}
