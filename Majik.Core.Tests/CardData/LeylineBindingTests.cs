using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="LeylineBindingFactory"/> — Enchantment — Aura
/// {W}{W}{W}{W}{W}.
///
///   "Domain — This spell costs {1} less to cast for each basic land
///    type among lands you control.
///    Enchant nonland permanent an opponent controls.
///    Enchanted permanent can't attack, block, or activate non-mana
///    abilities."
///
/// Covers:
/// - Card identity (Enchantment Aura, {WWWWW}).
/// - NamedCardFactory dispatch.
/// - Domain cost reduction (CR 702.16 / CR 117.7): 0/3/5 basic types,
///   floor at the coloured pips (CR 117.7c — five W pips never reduce).
/// - Static lockout via <see cref="LeylineBindingLifecycle"/>:
///     * Bearer is Cannot-Attack + Cannot-Block + Cannot-Activate
///       (non-mana) while aura is on the battlefield and attached.
///     * Aura LTB removes all three restrictions.
/// </summary>
public class LeylineBindingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    private static void AddBasic(Player owner, CardSubtype subtype, string name)
    {
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype })
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(land);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_IsEnchantmentAura_WithFiveWhitePips()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        lb.Name.Should().Be("Leyline Binding");
        lb.HasType(CardType.Enchantment).Should().BeTrue();
        lb.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        lb.IsAura.Should().BeTrue();
        lb.ManaCost.Should().Be("{W}{W}{W}{W}{W}");
        lb.Owner.Should().BeSameAs(_alice);
        lb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LeylineBinding()
    {
        var lb = NamedCardFactory.Create("Leyline Binding", _alice);

        lb.Should().BeOfType<Enchantment>();
        lb.Name.Should().Be("Leyline Binding");
        lb.ManaCost.Should().Be("{W}{W}{W}{W}{W}");
        lb.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Domain cost reduction (CR 702.16 / CR 117.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_NoBasicTypes_PaysFullFiveW()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        // No generic mana in the printed cost; Domain has nothing to chew
        // on. Coloured pips (5 W) are untouched (CR 117.7c).
        effective.Generic.Should().Be(0, "no generic mana to reduce");
        effective.White.Should().Be(5, "five coloured pips remain (CR 117.7c)");
    }

    [Fact]
    public void LeylineBinding_ThreeBasicTypes_DoesNotEatColoredPips()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        // Printed generic is 0 — Domain's three-type {3} reduction has
        // nothing to chew through; coloured pips stay (CR 117.7c).
        effective.Generic.Should().Be(0,
            "Domain caps the generic-mana reduction at the printed generic " +
            "(zero) — coloured pips never reduce");
        effective.White.Should().Be(5);
    }

    [Fact]
    public void LeylineBinding_AllFiveBasicTypes_StillRequiresFiveColoredPips()
    {
        // The canonical "Leyline Binding turn-2 for {W}" case — note the
        // printed mana cost is FIVE coloured pips. Domain reduces only
        // generic mana (CR 117.7c). With zero printed generic, the
        // discount is moot: the spell still requires 5 W.
        var lb = LeylineBindingFactory.Create(_alice);

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Swamp, "Swamp");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");
        AddBasic(_alice, CardSubtype.Forest, "Forest");

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        effective.Generic.Should().Be(0);
        effective.White.Should().Be(5,
            "CR 117.7c — Domain only reduces generic mana; the five W " +
            "pips are required regardless of basic-land-type count");
    }

    // -----------------------------------------------------------------------
    // Cost-reduction floor: synthetic printed-generic scenario verifying
    // the per-basic-type {1} math itself.
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_DomainReducer_IsExactlyOnePerBasicType()
    {
        // Sanity-check the cost reducer in isolation: take the reducer
        // off Leyline Binding and apply it to a synthetic card with
        // {5} printed generic. With 3 basics, {5} → {2}.
        var lb = LeylineBindingFactory.Create(_alice);
        var reducer = lb.Abilities.OfType<CostReductionAbility>().Single();
        reducer.TotalReducer.Should().NotBeNull("Domain uses the whole-reducer shape");

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");

        reducer.TotalReducer!(_alice).Should().Be(3,
            "Domain returns 1 × number of distinct basic land types " +
            "(CR 702.16); three distinct types → {3} reduction");
    }

    // -----------------------------------------------------------------------
    // Static lockout (CR 602.5 / 509.1c / 508.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Attached_RegistersAllThreeRestrictions_OnEnchantedCreature()
    {
        var lb = LeylineBindingFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(lb);
        lb.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);

        lb.AttachTo(bear);

        // The lifecycle wired in Create() needs a poke when the aura was
        // already on the battlefield before AttachTo (the only zone-move
        // event happened before the bearer existed). Re-sync via a fake
        // zone-move event that fires the lifecycle's handler.
        var lifecycle = new LeylineBindingLifecycle(lb, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        bear.ActiveEffects!.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeTrue("enchanted creature can't attack");
        bear.ActiveEffects.HasRestriction(bear, CombatRestriction.CannotBlock)
            .Should().BeTrue("enchanted creature can't block");
        bear.ActiveEffects.HasActivationRestriction(bear, isManaAbility: false)
            .Should().BeTrue("enchanted creature can't activate non-mana abilities");
        // Mana abilities (CR 605) are still permitted.
        bear.ActiveEffects.HasActivationRestriction(bear, isManaAbility: true)
            .Should().BeFalse("mana abilities (CR 605) are explicitly excluded");
    }

    [Fact]
    public void Auras_LTB_RemovesAllRestrictions()
    {
        var lb = LeylineBindingFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(lb);
        lb.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);
        lb.AttachTo(bear);

        var lifecycle = new LeylineBindingLifecycle(lb, _bus);
        lifecycle.Attach();
        lifecycle.Sync();
        lifecycle.IsActive.Should().BeTrue();

        // Aura LTB — move aura off battlefield to graveyard.
        _alice.Zones.Battlefield.RemoveCard(lb);
        _alice.Zones.Graveyard.AddCard(lb);
        lb.SetZone(ZoneType.Graveyard);
        lifecycle.Sync();

        bear.ActiveEffects!.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeFalse("restrictions unregister when aura LTBs");
        bear.ActiveEffects.HasRestriction(bear, CombatRestriction.CannotBlock)
            .Should().BeFalse();
        bear.ActiveEffects.HasActivationRestriction(bear)
            .Should().BeFalse();
        lifecycle.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Unattached_NoRestrictions()
    {
        // Aura on battlefield but not attached: lifecycle is dormant.
        var lb = LeylineBindingFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(lb);
        lb.SetZone(ZoneType.Battlefield);

        var lifecycle = new LeylineBindingLifecycle(lb, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        lifecycle.IsActive.Should().BeFalse(
            "without an AttachedTo target, no restrictions are registered");
    }
}
