using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Moq;
using Xunit;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// CR 711 / 306.5b / 704.5j — the transient ("effective") loyalty surface on
/// <see cref="Permanent"/>. A creature-front transform DFC whose BACK face is a
/// planeswalker (Ral, Monsoon Mage // Ral, Leyline Prodigy) is a
/// <see cref="Creature"/> C# instance, NOT a <see cref="Planeswalker"/>.
/// Flipping to the back face grants it a working loyalty body via the
/// <see cref="BackFaceCharacteristics.Loyalty"/> seed — without re-classing the
/// runtime object — so loyalty-removing damage (CR 306.7 / 120.3) and the
/// loyalty=0 death SBA (CR 704.5j) apply to the back face. Flipping back clears
/// the body.
/// </summary>
public class TransientLoyaltyBodyTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ContinuousEffectsService Wire(Creature c)
    {
        var ces = new ContinuousEffectsService();
        c.ActiveEffects = ces;
        return ces;
    }

    // ------------------------------------------------------------------
    // Permanent surface primitives (subclass-agnostic).
    // ------------------------------------------------------------------

    [Fact]
    public void Creature_WithNoTransientLoyalty_HasNoLoyaltyBody()
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);
        c.GetEffectiveLoyalty().Should().BeNull();
        c.IsEffectivePlaneswalker().Should().BeFalse();
        c.IsLoyaltyDead().Should().BeFalse("a creature with no loyalty body never dies to the PW SBA");
    }

    [Fact]
    public void SetTransientLoyalty_GrantsAndClearsLoyaltyBody()
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);

        c.SetTransientLoyalty(3);
        c.GetEffectiveLoyalty().Should().Be(3);
        c.IsEffectivePlaneswalker().Should().BeTrue();
        c.IsLoyaltyDead().Should().BeFalse();

        c.SetTransientLoyalty(null);
        c.GetEffectiveLoyalty().Should().BeNull();
        c.IsEffectivePlaneswalker().Should().BeFalse();
    }

    [Fact]
    public void RemoveTransientLoyalty_FloorsAtZero_AndMarksDead()
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);
        c.SetTransientLoyalty(2);

        c.RemoveTransientLoyalty(1).Should().BeTrue("a transient body absorbed the removal");
        c.GetEffectiveLoyalty().Should().Be(1);
        c.IsLoyaltyDead().Should().BeFalse();

        c.RemoveTransientLoyalty(5).Should().BeTrue();
        c.GetEffectiveLoyalty().Should().Be(0, "loyalty floors at 0");
        c.IsLoyaltyDead().Should().BeTrue("0 loyalty trips the PW death SBA (CR 704.5j)");
    }

    [Fact]
    public void RemoveTransientLoyalty_NoBody_IsNoOp()
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);
        c.RemoveTransientLoyalty(3).Should().BeFalse("no body to remove from");
        c.GetEffectiveLoyalty().Should().BeNull();
    }

    [Fact]
    public void RealPlaneswalker_EffectiveLoyalty_IsItsOwnField_NotTransient()
    {
        var pw = new Planeswalker("Jace", "{2}{U}{U}", 3) { Owner = _alice, Controller = _alice };
        pw.GetEffectiveLoyalty().Should().Be(3);
        pw.IsEffectivePlaneswalker().Should().BeTrue();

        // The transient setter is inert on a real planeswalker — it keeps its
        // own authoritative loyalty field.
        pw.SetTransientLoyalty(99);
        pw.GetEffectiveLoyalty().Should().Be(3, "a real planeswalker reads its own loyalty");

        pw.RemoveTransientLoyalty(3).Should().BeTrue();
        pw.GetEffectiveLoyalty().Should().Be(0);
        pw.IsLoyaltyDead().Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Transform wiring: creature-front ↔ planeswalker-back loyalty body.
    // ------------------------------------------------------------------

    [Fact]
    public void Ral_FrontFace_HasNoLoyaltyBody()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);
        ral.IsEffectivePlaneswalker().Should().BeFalse("the played front face is a creature");
        ral.GetEffectiveLoyalty().Should().BeNull();
    }

    [Fact]
    public void Ral_TransformToBack_GrantsLoyaltyTwoBody()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);
        Wire(ral);

        ral.MdfcState!.Transform(); // → Ral, Leyline Prodigy

        ral.IsEffectivePlaneswalker().Should().BeTrue("the back face is a planeswalker");
        ral.GetEffectiveLoyalty().Should().Be(2, "Ral, Leyline Prodigy enters with loyalty 2");
    }

    [Fact]
    public void Ral_BackFace_SeedsPlaneswalkerTypeThroughCompute()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);
        var ces = Wire(ral);

        ral.MdfcState!.Transform();

        var chars = ces.Compute((Permanent)ral);
        chars.Types.Should().Contain(CardType.Planeswalker, "the back-face Layer-0 seed stamps the PW type");
        chars.Subtypes.Should().Contain(CardSubtype.Ral);
        chars.Colors.Should().Contain(ManaColorEnum.Blue);
        chars.Colors.Should().Contain(ManaColorEnum.Red);
    }

    [Fact]
    public void Ral_TransformBack_ClearsLoyaltyBody()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);
        Wire(ral);

        ral.MdfcState!.Transform();          // back: loyalty body present
        ral.IsEffectivePlaneswalker().Should().BeTrue();

        ral.MdfcState!.Transform();          // front again
        ral.IsEffectivePlaneswalker().Should().BeFalse("flipping back to the creature front clears loyalty");
        ral.GetEffectiveLoyalty().Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Damage redirect: loyalty-removing damage to a transient body.
    // ------------------------------------------------------------------

    [Fact]
    public void Ral_BackFace_TakesDamageAsLoyaltyRemoval()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);
        Wire(ral);
        ral.MdfcState!.Transform(); // loyalty 2 PW back

        Fx.DealDamageAny(ral, 1);
        ral.GetEffectiveLoyalty().Should().Be(1, "1 damage removes 1 loyalty (CR 306.7)");
        ral.WasDealtDamageThisTurn.Should().BeTrue("CR 120.3 — was dealt damage this turn");

        Fx.DealDamageAny(ral, 5);
        ral.GetEffectiveLoyalty().Should().Be(0, "loyalty floors at 0");
        ral.IsLoyaltyDead().Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Death SBA: a transient-loyalty body at 0 dies (CR 704.5j).
    // ------------------------------------------------------------------

    [Fact]
    public void DeathSba_DestroysTransientLoyaltyBodyAtZero()
    {
        var eventBus = new Mock<IEventBus>();
        var zoneService = new ZoneService(eventBus.Object);
        var sba = new StateBasedActions(eventBus.Object, zoneService);

        var ral = RalMonsoonMageFactory.Create(_alice);
        ral.SetOwner(_alice);
        ral.SetController(_alice);
        ral.SetZone(ZoneType.Battlefield);
        zoneService.MoveCardTo(ral, ZoneType.Battlefield, _alice);
        ral.MdfcState!.Transform();          // loyalty 2 back face
        ral.RemoveTransientLoyalty(2);        // → 0 loyalty

        sba.CheckStateBasedActions(new List<Player> { _alice }, new List<ICard> { ral });

        ral.Zone.Should().Be(ZoneType.Graveyard, "a 0-loyalty back-face planeswalker dies (CR 704.5j)");
    }

    [Fact]
    public void DeathSba_LeavesCreatureWithNoLoyaltyBodyAlone()
    {
        var eventBus = new Mock<IEventBus>();
        var zoneService = new ZoneService(eventBus.Object);
        var sba = new StateBasedActions(eventBus.Object, zoneService);

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        zoneService.MoveCardTo(bear, ZoneType.Battlefield, _alice);

        sba.CheckStateBasedActions(new List<Player> { _alice }, new List<ICard> { bear });

        bear.Zone.Should().Be(ZoneType.Battlefield, "a plain creature carries no loyalty body and is not touched by the PW death SBA");
    }
}
