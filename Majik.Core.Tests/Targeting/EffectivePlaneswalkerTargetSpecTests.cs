using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.TargetingPipeline;

/// <summary>
/// CR 115.4 / 711 / 306.7 — the declarative targeting-OFFER layer
/// (<see cref="TargetSpec.Matches"/> → <see cref="TargetLegality"/>) must
/// classify a creature-front transform DFC flipped to its planeswalker BACK
/// face (Ral, Monsoon Mage // Ral, Leyline Prodigy) by its EFFECTIVE
/// (layer-computed) types, NOT its lingering printed <c>HasType(Creature)</c>
/// flag — which still reads true on the flipped Creature C# instance.
///
/// <para>
/// A flipped effective planeswalker (<see cref="Permanent.IsEffectivePlaneswalker"/>)
/// must be OFFERED by a "target planeswalker" / "any target" spec and must NOT
/// be offered by a "target creature"-only spec. This is the candidate-gather
/// half of the v1-deferral; the damage-APPLICATION half
/// (<see cref="Primitives.Fx.DealDamageAny"/>) already routes it to transient
/// loyalty removal. Until this lands, a "target planeswalker" removal spell can
/// never SELECT a flipped effective planeswalker in the first place.
/// </para>
/// </summary>
public class EffectivePlaneswalkerTargetSpecTests
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
    public void FlippedRal_IsPlaneswalkerSpecTarget_NotCreatureSpecTarget()
    {
        var ral = FlippedRal(_alice);

        new TargetSpec("planeswalker").Planeswalkers().Matches(ral)
            .Should().BeTrue("the back face carries an effective loyalty body (CR 711 / 306.5b)");
        new TargetSpec("creature").Creatures().Matches(ral)
            .Should().BeFalse("a planeswalker back face is no longer effectively a creature (CR 711)");
    }

    [Fact]
    public void FlippedRal_IsAnyTargetSpecTarget()
    {
        var ral = FlippedRal(_alice);

        new TargetSpec("any").AnyTarget().Matches(ral)
            .Should().BeTrue("an effective planeswalker is a legal 'any target' (CR 115.4)");
    }

    [Fact]
    public void FlippedRal_IsLegalPlaneswalkerTarget_AndEnumerated()
    {
        var ral = FlippedRal(_alice);
        _alice.Zones.Battlefield.AddCard(ral);

        var spec = new TargetSpec("planeswalker").Planeswalkers();
        TargetLegality.IsLegal(spec, ral, _alice).Should().BeTrue();
        TargetLegality.EnumerateLegal(spec, _alice, new[] { _alice })
            .Should().Contain(ral, "a flipped effective planeswalker is enumerated as a planeswalker candidate");
    }

    [Fact]
    public void FrontFaceRal_IsCreatureSpecTarget_NotPlaneswalker()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);
        ((Permanent)ral).ActiveEffects = new ContinuousEffectsService();
        ral.SetZone(ZoneType.Battlefield);
        // not flipped — still the creature front face

        new TargetSpec("creature").Creatures().Matches(ral).Should().BeTrue();
        new TargetSpec("planeswalker").Planeswalkers().Matches(ral).Should().BeFalse();
    }

    [Fact]
    public void RealPlaneswalker_IsPlaneswalkerSpecTarget_NotCreature()
    {
        var jace = new Planeswalker("Jace", "{2}{U}{U}", 3) { Owner = _alice, Controller = _alice };
        jace.SetZone(ZoneType.Battlefield);

        new TargetSpec("planeswalker").Planeswalkers().Matches(jace).Should().BeTrue();
        new TargetSpec("creature").Creatures().Matches(jace).Should().BeFalse();
    }

    [Fact]
    public void PlainCreature_IsCreatureSpecTarget_NotPlaneswalker()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);

        new TargetSpec("creature").Creatures().Matches(bear).Should().BeTrue();
        new TargetSpec("planeswalker").Planeswalkers().Matches(bear).Should().BeFalse();
    }
}
