using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.TargetingPipeline;

/// <summary>
/// CR 115.4 / 711 / 306.7 — the "any target" damage-targeting layer must
/// classify a creature-front transform DFC flipped to its planeswalker BACK
/// face (Ral, Monsoon Mage // Ral, Leyline Prodigy) by its EFFECTIVE
/// (layer-computed) types, NOT its lingering printed Creature instance type.
///
/// A flipped effective planeswalker (<see cref="Permanent.IsEffectivePlaneswalker"/>)
/// is no longer effectively a creature (<see cref="Permanent.IsEffectivelyCreature"/>
/// is false), so burn that says "any target" must offer it as a PLANESWALKER —
/// and a "target creature"-only spell must NOT offer it. The damage application
/// (<see cref="Primitives.Fx.DealDamageAny"/>) already routes it to transient
/// loyalty removal; this is the targeting-offering half the v1-deferral pairs
/// with.
/// </summary>
public class EffectivePlaneswalkerDamageTargetTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature FlippedRal(Player owner)
    {
        var ral = RalMonsoonMageFactory.Create(owner);
        ((Permanent)ral).ActiveEffects = new ContinuousEffectsService();
        ral.SetZone(ZoneType.Battlefield);
        ral.MdfcState!.Transform(); // → Ral, Leyline Prodigy (planeswalker back)
        return ral;
    }

    [Fact]
    public void FlippedRal_IsAnyDamageTarget()
    {
        var ral = FlippedRal(_alice);
        DamageTargeting.IsAnyDamageTarget(ral)
            .Should().BeTrue("an effective planeswalker is a legal 'any target' damage target (CR 115.4)");
    }

    [Fact]
    public void FlippedRal_IsClassifiedAsPlaneswalker_NotCreature()
    {
        var ral = FlippedRal(_alice);

        DamageTargeting.IsCreatureDamageTarget(ral)
            .Should().BeFalse("a planeswalker back face is no longer effectively a creature (CR 711)");
        DamageTargeting.IsPlaneswalkerDamageTarget(ral)
            .Should().BeTrue("the back face carries an effective loyalty body (CR 711 / 306.5b)");
    }

    [Fact]
    public void FrontFaceRal_IsCreatureTarget_NotPlaneswalker()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);
        ((Permanent)ral).ActiveEffects = new ContinuousEffectsService();
        ral.SetZone(ZoneType.Battlefield);
        // not flipped — still the creature front face

        DamageTargeting.IsCreatureDamageTarget(ral).Should().BeTrue();
        DamageTargeting.IsPlaneswalkerDamageTarget(ral).Should().BeFalse();
        DamageTargeting.IsAnyDamageTarget(ral).Should().BeTrue();
    }

    [Fact]
    public void PlainCreature_IsCreatureTarget_NotPlaneswalker()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);

        DamageTargeting.IsCreatureDamageTarget(bear).Should().BeTrue();
        DamageTargeting.IsPlaneswalkerDamageTarget(bear).Should().BeFalse();
        DamageTargeting.IsAnyDamageTarget(bear).Should().BeTrue();
    }

    [Fact]
    public void RealPlaneswalker_IsPlaneswalkerTarget_NotCreature()
    {
        var jace = new Planeswalker("Jace", "{2}{U}{U}", 3) { Owner = _alice, Controller = _alice };
        jace.SetZone(ZoneType.Battlefield);

        DamageTargeting.IsCreatureDamageTarget(jace).Should().BeFalse();
        DamageTargeting.IsPlaneswalkerDamageTarget(jace).Should().BeTrue();
        DamageTargeting.IsAnyDamageTarget(jace).Should().BeTrue();
    }

    [Fact]
    public void PlainLand_IsNotAnyDamageTarget()
    {
        var forest = new Land("Forest") { Owner = _alice, Controller = _alice };
        forest.SetZone(ZoneType.Battlefield);

        DamageTargeting.IsAnyDamageTarget(forest).Should().BeFalse();
    }
}
