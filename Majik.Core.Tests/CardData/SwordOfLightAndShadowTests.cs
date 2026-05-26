using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SwordOfLightAndShadowFactory"/> (Darksteel, {3}).
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch + Equipment shape.
/// - Equip activated ability shape: {2} mana cost.
/// - Static +2/+2 effect via the runtime overload + ContinuousEffectsService.
/// - Protection markers: "white" + "black" ProtectionAbility instances
///   present on the shape-only path; Protection.HasProtectionFromColor
///   answers true for both.
/// - Combat-damage-to-a-player trigger: condition gates on equipped
///   creature + non-null TargetPlayer; resolution gains 3 life and returns
///   a chosen creature card from controller's graveyard to hand.
/// </summary>
public class SwordOfLightAndShadowTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfLightAndShadow_Identity()
    {
        var c = SwordOfLightAndShadowFactory.Create(_alice);

        c.Name.Should().Be("Sword of Light and Shadow");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfLightAndShadow_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of Light and Shadow", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sword of Light and Shadow");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage-to-a-player trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Equip {2} is wired");
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2,
            "protection from white + black markers ride on the equipment");
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfLightAndShadow_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfLightAndShadowFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Static +2/+2
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfLightAndShadow_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfLightAndShadowFactory.Create(_alice, svc, triggers: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // Protection markers
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfLightAndShadow_HasProtectionFromWhiteAndBlack_Markers()
    {
        var sword = SwordOfLightAndShadowFactory.Create(_alice);

        var qualities = sword.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "white", "black" });

        Protection.HasProtectionFromColor(sword, ManaColor.White).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Black).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Red).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Combat-damage trigger — gating
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfLightAndShadow_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var other = new Creature("Other", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfLightAndShadowFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue("equipped creature damages a player");
        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse("unequipped creature does not fire it");
        var dummy = new Creature("Dummy", "1G", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, dummy, 2))
            .Should().BeFalse("damage to a creature does not fire it");
    }

    // -----------------------------------------------------------------------
    // Combat-damage trigger — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfLightAndShadow_CombatTrigger_GainsThreeLife_AndReturnsCreatureFromGraveyard()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfLightAndShadowFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        // Seed Alice's graveyard with a creature card.
        var dead = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(dead);
        dead.SetZone(ZoneType.Graveyard);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { dead },
        });

        _alice.LifeTotal.Should().Be(20);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(23,
            "controller gains 3 life on combat-damage trigger");
        _alice.Zones.Hand.GetCards().Should().Contain(dead,
            "the chosen creature card returns to the controller's hand");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(dead,
            "the returned card leaves the graveyard");
    }

    [Fact]
    public void SwordOfLightAndShadow_CombatTrigger_NoTarget_StillGainsLife()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfLightAndShadowFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        // No chosen target — the "may" + "up to one" half is declined.

        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(23,
            "life-gain half is mandatory and still resolves with no target");
        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no target → no bounce");
    }
}
