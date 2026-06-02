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
/// Tests for <see cref="CraterhoofBehemothFactory"/> (Avacyn Restored,
/// {5}{G}{G}{G}).
///
/// Covers:
/// - Identity + named-factory dispatch (Beast 5/5 {5}{G}{G}{G}).
/// - Intrinsic Haste marker (CR 702.10).
/// - Single ETB triggered ability (CR 603.6a).
/// - ETB rider: creatures the controller controls gain Trample and get +X/+X
///   where X = number of creatures the controller controls, snapshotted at
///   resolution (CR 608.2). Craterhoof itself is included in X. Opponent
///   creatures are untouched.
/// </summary>
[Trait("Color", "G")]
public class CraterhoofBehemothFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Identity()
    {
        var c = CraterhoofBehemothFactory.Create(_alice);

        c.Name.Should().Be("Craterhoof Behemoth");
        c.ManaCost.Should().Be("{5}{G}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Power.Should().Be(5);
        c.Toughness.Should().Be(5);
        c.Owner.Should().Be(_alice);
        c.Controller.Should().Be(_alice);
    }

    [Fact]
    public void HasIntrinsicHaste()
    {
        var c = CraterhoofBehemothFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Haste")
            .Should().BeTrue("Craterhoof Behemoth has intrinsic Haste (CR 702.10)");
        CombatAbilities.HasHaste(c).Should().BeTrue();
    }

    [Fact]
    public void HasSingleEtbTriggeredAbility()
    {
        var c = CraterhoofBehemothFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "ETB trample + dynamic +X/+X trigger");
    }

    [Fact]
    public void EtbTrigger_GrantsTrampleAndPumpsByCreatureCount_IncludingItself()
    {
        // Alice controls two other creatures + Craterhoof = 3 creatures, so
        // X = 3: every Alice creature gains Trample and gets +3/+3.
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

        var hoof = CraterhoofBehemothFactory.Create(_alice);
        hoof.ActiveEffects = effects;
        hoof.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hoof);

        // Fire the ETB trigger body.
        var etb = hoof.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        // X = 3 (bear, elf, Craterhoof). Each Alice creature: +3/+3 + Trample.
        bear.GetPower().Should().Be(5);
        bear.GetToughness().Should().Be(5);
        CombatAbilities.HasTrample(bear).Should().BeTrue();

        elf.GetPower().Should().Be(4);
        elf.GetToughness().Should().Be(4);
        CombatAbilities.HasTrample(elf).Should().BeTrue();

        // Craterhoof itself is counted and pumped: 5/5 base + 3/3 = 8/8.
        hoof.GetPower().Should().Be(8);
        hoof.GetToughness().Should().Be(8);
        CombatAbilities.HasTrample(hoof).Should().BeTrue();

        // Bob's creature untouched.
        bobBear.GetPower().Should().Be(2);
        bobBear.GetToughness().Should().Be(2);
        CombatAbilities.HasTrample(bobBear).Should().BeFalse();
    }

    [Fact]
    public void EtbTrigger_OnlyCraterhoof_PumpsByOne()
    {
        // Craterhoof alone on the battlefield → X = 1 (it counts itself).
        var effects = new ContinuousEffectsService();

        var hoof = CraterhoofBehemothFactory.Create(_alice);
        hoof.ActiveEffects = effects;
        hoof.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hoof);

        var etb = hoof.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        hoof.GetPower().Should().Be(6, "5/5 base + X(=1)");
        hoof.GetToughness().Should().Be(6);
        CombatAbilities.HasTrample(hoof).Should().BeTrue();
    }

    [Fact]
    public void EtbTrigger_NoControlledCreatures_NoOpsCleanly()
    {
        // Craterhoof not on the battlefield, no creatures controlled → no-op.
        var hoof = CraterhoofBehemothFactory.Create(_alice);

        var etb = hoof.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
    }
}
