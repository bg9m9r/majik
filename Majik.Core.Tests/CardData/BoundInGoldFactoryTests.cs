using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="BoundInGoldFactory"/> — Enchantment — Aura
/// {2}{W} (Kaldheim).
///
///   "Enchant permanent
///    Enchanted permanent can't attack, block, or crew Vehicles, and
///    its activated abilities can't be activated unless they're mana
///    abilities."
///
/// Covers:
/// - Card identity (Enchantment Aura, {2}{W}).
/// - NamedCardFactory dispatch.
/// - Static lockout via <see cref="BoundInGoldLifecycle"/>:
///     * Bearer is CannotAttack + CannotBlock + ActivationRestriction
///       (non-mana) while aura is on the battlefield and attached.
///     * Aura LTB removes all three restrictions.
///     * Unattached aura registers nothing.
/// </summary>
public class BoundInGoldFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BoundInGold_IsEnchantmentAura_WithTwoWhitePlusGeneric()
    {
        var bg = BoundInGoldFactory.Create(_alice);

        bg.Name.Should().Be("Bound in Gold");
        bg.HasType(CardType.Enchantment).Should().BeTrue();
        bg.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        bg.IsAura.Should().BeTrue();
        bg.ManaCost.Should().Be("{2}{W}");
        bg.Owner.Should().BeSameAs(_alice);
        bg.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BoundInGold()
    {
        var bg = NamedCardFactory.Create("Bound in Gold", _alice);

        bg.Should().BeOfType<Enchantment>();
        bg.Name.Should().Be("Bound in Gold");
        bg.ManaCost.Should().Be("{2}{W}");
        bg.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static lockout (CR 602.5 / 509.1c / 508.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Attached_RegistersAllThreeRestrictions_OnEnchantedCreature()
    {
        var bg = BoundInGoldFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(bg);
        bg.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);

        bg.AttachTo(bear);

        // Re-sync via a fresh lifecycle (mirrors LeylineBindingTests
        // pattern — the aura was already on the battlefield before
        // AttachTo, so Attach() and Sync() are exposed for tests).
        var lifecycle = new BoundInGoldLifecycle(bg, _bus);
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
        var bg = BoundInGoldFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(bg);
        bg.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _bob.Zones.Battlefield.AddCard(bear);
        bg.AttachTo(bear);

        var lifecycle = new BoundInGoldLifecycle(bg, _bus);
        lifecycle.Attach();
        lifecycle.Sync();
        lifecycle.IsActive.Should().BeTrue();

        // Aura LTB — move aura off battlefield to graveyard.
        _alice.Zones.Battlefield.RemoveCard(bg);
        _alice.Zones.Graveyard.AddCard(bg);
        bg.SetZone(ZoneType.Graveyard);
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
        var bg = BoundInGoldFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(bg);
        bg.SetZone(ZoneType.Battlefield);

        var lifecycle = new BoundInGoldLifecycle(bg, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        lifecycle.IsActive.Should().BeFalse(
            "without an AttachedTo target, no restrictions are registered");
    }

    [Fact]
    public void NonCreatureBearer_NoRestrictions()
    {
        // Bound in Gold's printed scope is "Enchant permanent" — it CAN
        // attach to non-creature permanents on the battlefield. v1's
        // ContinuousEffectsService is creature-only, so non-creature
        // bearers silently no-op (documented gap; same posture as
        // LeylineBindingLifecycle).
        var bg = BoundInGoldFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(bg);
        bg.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Random artifact", "{0}")
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        _bob.Zones.Battlefield.AddCard(artifact);

        bg.AttachTo(artifact);

        var lifecycle = new BoundInGoldLifecycle(bg, _bus);
        lifecycle.Attach();
        lifecycle.Sync();

        lifecycle.IsActive.Should().BeFalse(
            "non-creature bearer has no per-permanent ActiveEffects in v1; restrictions silently no-op");
    }
}
