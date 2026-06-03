using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StaticDischargeFactory"/> (Mystery Booster 2,
/// {1}{R}).
///
/// Scryfall oracle (verbatim):
///   "Starting intensity 3
///    This sorcery deals damage equal to its intensity to any target. Then
///    cards you own named Static Discharge intensify by 1."
///
/// Covers the Intensity / Intensify mechanic end-to-end:
/// - Identity ({1}{R} Sorcery), starting intensity 3 + keyword marker.
/// - Spell definition shape: 1..1 "any target".
/// - First cast deals 3 (the starting intensity); afterwards every owned copy
///   intensifies to 4.
/// - Second cast deals 4 (the accumulated intensity), then intensifies to 5.
/// - Damage routes through any-target so a creature target takes damage.
/// </summary>
[Trait("Color", "R")]
public class StaticDischargeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void StaticDischarge_Identity_SorceryAt1R()
    {
        var sd = StaticDischargeFactory.Create(_alice);

        sd.Name.Should().Be("Static Discharge");
        sd.HasType(CardType.Sorcery).Should().BeTrue();
        sd.ManaCost.ToString().Should().Be("{1}{R}");
        sd.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StaticDischarge_Create_StampsStartingIntensity3()
    {
        var sd = StaticDischargeFactory.Create(_alice);

        sd.Intensity.Should().Be(3, "Starting intensity 3");
        sd.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Intensity 3");
    }

    [Fact]
    public void StaticDischarge_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = StaticDischargeFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void StaticDischarge_FirstCast_Deals3ThenIntensifiesTo4()
    {
        // Put the resolving spell on the stack (owned by Alice) so IntensityOf
        // finds it.
        var sd = StaticDischargeFactory.Create(_alice);
        sd.SetZone(ZoneType.Stack);
        _alice.Zones.GetZone(ZoneType.Stack).AddCard(sd);

        var def = StaticDischargeFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(17, "intensity 3 → 3 damage on first cast");
        sd.Intensity.Should().Be(4, "then intensify by 1");
    }

    [Fact]
    public void StaticDischarge_SecondCast_DealsAccumulated4()
    {
        var sd = StaticDischargeFactory.Create(_alice);
        sd.SetZone(ZoneType.Stack);
        _alice.Zones.GetZone(ZoneType.Stack).AddCard(sd);

        var def = StaticDischargeFactory.BuildSpellDefinition(_alice, resolver: x => x);

        // First cast: 3 damage, intensity → 4.
        foreach (var e in def.EffectFactory(new ChosenSpellParams(
            null, null, new[] { (IReadOnlyList<object>)new object[] { _bob } }, ManaPayment.Empty)))
            e.Execute();

        // Second cast: reads the accumulated intensity (4).
        foreach (var e in def.EffectFactory(new ChosenSpellParams(
            null, null, new[] { (IReadOnlyList<object>)new object[] { _bob } }, ManaPayment.Empty)))
            e.Execute();

        _bob.LifeTotal.Should().Be(20 - 3 - 4, "second cast deals the accumulated intensity 4");
        sd.Intensity.Should().Be(5, "intensified again after the second cast");
    }

    [Fact]
    public void StaticDischarge_DamagesCreatureTarget()
    {
        var sd = StaticDischargeFactory.Create(_alice);
        sd.SetZone(ZoneType.Stack);
        _alice.Zones.GetZone(ZoneType.Stack).AddCard(sd);

        var wall = new Creature("Wall of Wood", "{G}", 0, 6,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = StaticDischargeFactory.BuildSpellDefinition(_alice, resolver: x => x);
        foreach (var e in def.EffectFactory(new ChosenSpellParams(
            null, null, new[] { (IReadOnlyList<object>)new object[] { wall } }, ManaPayment.Empty)))
            e.Execute();

        wall.Damage.Should().Be(3, "intensity 3 damage to a creature target");
    }
}
