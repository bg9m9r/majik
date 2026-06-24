using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MoonshakerCavalryFactory"/> (Wilds of Eldraine,
/// {5}{W}{W}{W}).
///
/// Covers:
/// - Identity (Spirit Knight 6/6 {5}{W}{W}{W}).
/// - Intrinsic Flying marker (CR 702.9).
/// - Single ETB triggered ability (CR 603.6a).
/// - ETB rider: creatures the controller controls gain Flying and get +X/+X
///   where X = number of creatures the controller controls, snapshotted at
///   resolution (CR 608.2). Moonshaker itself is included in X. Opponent
///   creatures are untouched.
/// </summary>
[Trait("Color", "W")]
public class MoonshakerCavalryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Identity()
    {
        var c = MoonshakerCavalryFactory.Create(_alice);

        c.Name.Should().Be("Moonshaker Cavalry");
        c.ManaCost.Should().Be("{5}{W}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.Owner.Should().Be(_alice);
        c.Controller.Should().Be(_alice);
    }

    [Fact]
    public void HasIntrinsicFlying()
    {
        var c = MoonshakerCavalryFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying")
            .Should().BeTrue("Moonshaker Cavalry has intrinsic Flying (CR 702.9)");
        CombatAbilities.HasFlying(c).Should().BeTrue();
    }

    [Fact]
    public void HasSingleEtbTriggeredAbility()
    {
        var c = MoonshakerCavalryFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "ETB flying + dynamic +X/+X trigger");
    }

    [Fact]
    public void EtbTrigger_GrantsFlyingAndPumpsByCreatureCount_IncludingItself()
    {
        // Alice controls two other creatures + Moonshaker = 3 creatures, so
        // X = 3: every Alice creature gains Flying and gets +3/+3.
        var effects = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var elf = new Creature("Llanowar Elves", "G", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(elf);

        var bobBear = new Creature("Bob's Bear", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _bob.Zones.Battlefield.AddCard(bobBear);

        var cavalry = MoonshakerCavalryFactory.Create(_alice);
        cavalry.ActiveEffects = effects;
        cavalry.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cavalry);

        // Fire the ETB trigger body.
        var etb = cavalry.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        // X = 3 (bear, elf, Moonshaker). Each Alice creature: +3/+3 + Flying.
        bear.GetPower().Should().Be(5);
        bear.GetToughness().Should().Be(5);
        CombatAbilities.HasFlying(bear).Should().BeTrue();

        elf.GetPower().Should().Be(4);
        elf.GetToughness().Should().Be(4);
        CombatAbilities.HasFlying(elf).Should().BeTrue();

        // Moonshaker itself is counted and pumped: 6/6 base + 3/3 = 9/9.
        cavalry.GetPower().Should().Be(9);
        cavalry.GetToughness().Should().Be(9);
        CombatAbilities.HasFlying(cavalry).Should().BeTrue();

        // Bob's creature untouched.
        bobBear.GetPower().Should().Be(2);
        bobBear.GetToughness().Should().Be(2);
        CombatAbilities.HasFlying(bobBear).Should().BeFalse();
    }

    [Fact]
    public void EtbTrigger_OnlyMoonshaker_PumpsByOne()
    {
        // Moonshaker alone on the battlefield → X = 1 (it counts itself).
        var effects = new ContinuousEffectsService();

        var cavalry = MoonshakerCavalryFactory.Create(_alice);
        cavalry.ActiveEffects = effects;
        cavalry.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cavalry);

        var etb = cavalry.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        cavalry.GetPower().Should().Be(7, "6/6 base + X(=1)");
        cavalry.GetToughness().Should().Be(7);
        CombatAbilities.HasFlying(cavalry).Should().BeTrue();
    }

    [Fact]
    public void EtbTrigger_NoControlledCreatures_NoOpsCleanly()
    {
        // Moonshaker not on the battlefield, no creatures controlled → no-op.
        var cavalry = MoonshakerCavalryFactory.Create(_alice);

        var etb = cavalry.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
    }
}
