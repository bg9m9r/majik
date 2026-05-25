using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Sai of the Shinobi — Artifact — Equipment {1}
/// (Saviors of Kamigawa).
///
///   "Equipped creature has 'Whenever this creature deals damage, you
///    may untap target permanent.'"
///   "Equip {1}."
///
/// Covers:
/// - Identity (Artifact + Equipment subtype, mana cost {1}).
/// - NamedCardFactory dispatch.
/// - Equip {1} activated ability shape.
/// - Damage trigger present + matches when the equipped creature deals
///   damage (CR 603.1 / 119); does NOT match for other creatures' damage.
/// - Resolution: chosen target permanent is untapped (printed "may"
///   auto-accepted v1).
/// - Unattached Sai: damage event does not match (AttachedTo null).
/// </summary>
public class SaiOfTheShinobiFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SaiOfTheShinobi_Identity()
    {
        var s = SaiOfTheShinobiFactory.Create(_alice);

        s.Name.Should().Be("Sai of the Shinobi");
        s.ManaCost.Should().Be("{1}");
        s.HasType(CardType.Artifact).Should().BeTrue();
        s.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SaiOfTheShinobi_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sai of the Shinobi", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sai of the Shinobi");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    [Fact]
    public void SaiOfTheShinobi_EquipAbility_HasGenericOneCost()
    {
        var s = SaiOfTheShinobiFactory.Create(_alice);

        var equip = s.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.EquipCost.Generic.Should().Be(1, "printed Equip {1}");
    }

    [Fact]
    public void DamageTrigger_Matches_WhenEquippedCreatureDealsDamage()
    {
        var sai = SaiOfTheShinobiFactory.Create(_alice);
        sai.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        sai.AttachTo(bear);

        var trigger = sai.Abilities.OfType<TriggeredAbility>().Single();
        var damage = new CombatDamageDealtEvent(bear, _alice, 2);

        trigger.IsTriggered(damage).Should().BeTrue(
            "Sai's granted trigger fires when the equipped creature deals damage");
    }

    [Fact]
    public void DamageTrigger_DoesNotMatch_WhenADifferentCreatureDealsDamage()
    {
        var sai = SaiOfTheShinobiFactory.Create(_alice);
        sai.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        sai.AttachTo(bear);

        // A different creature deals damage — predicate must reject.
        var other = new Creature("Savannah Lions", "{W}", 2, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };

        var trigger = sai.Abilities.OfType<TriggeredAbility>().Single();
        var damage = new CombatDamageDealtEvent(other, _alice, 2);

        trigger.IsTriggered(damage).Should().BeFalse(
            "Sai's granted trigger only fires for the equipped creature");
    }

    [Fact]
    public void DamageTrigger_DoesNotMatch_WhenSaiIsUnattached()
    {
        var sai = SaiOfTheShinobiFactory.Create(_alice);
        sai.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        // intentionally not attached

        var trigger = sai.Abilities.OfType<TriggeredAbility>().Single();
        var damage = new CombatDamageDealtEvent(bear, _alice, 2);

        trigger.IsTriggered(damage).Should().BeFalse(
            "with no AttachedTo, the granted trigger has no host to fire from");
    }

    [Fact]
    public void DamageTrigger_OnResolve_UntapsChosenTappedPermanent()
    {
        var sai = SaiOfTheShinobiFactory.Create(_alice);
        sai.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        sai.AttachTo(bear);

        // Build a target that is currently tapped.
        var target = new Artifact("Sol Ring", "{1}")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        target.Tap();
        target.IsTapped.Should().BeTrue();

        var trigger = sai.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new[] { new object[] { target } });
        foreach (var effect in trigger.Effects) effect.Execute();

        target.IsTapped.Should().BeFalse("Sai's untap-target-permanent resolves");
    }

    [Fact]
    public void DamageTrigger_OnResolve_AlreadyUntappedTarget_IsNoOp()
    {
        // CR 701.20 — "untap" against an already-untapped permanent is a no-op.
        var sai = SaiOfTheShinobiFactory.Create(_alice);
        sai.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        sai.AttachTo(bear);

        var target = new Artifact("Sol Ring", "{1}")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        // already untapped
        target.IsTapped.Should().BeFalse();

        var trigger = sai.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new[] { new object[] { target } });
        foreach (var effect in trigger.Effects) effect.Execute();

        target.IsTapped.Should().BeFalse();
    }
}
