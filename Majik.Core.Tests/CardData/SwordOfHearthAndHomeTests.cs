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
/// Unit tests for <see cref="SwordOfHearthAndHomeFactory"/> (Modern
/// Horizons 2, {3}).
///
/// Covers:
/// - Identity (name, type, mana cost, Equipment subtype, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {2} activated ability cost.
/// - +2/+2 boost to equipped creature via AttachedBoostEffect (Layer 7c).
/// - Protection from green + white.
/// - Combat-damage-to-a-player trigger gating.
/// - Resolution: chosen creature is exiled + returned to battlefield;
///   basic land tutored from library to battlefield tapped.
/// </summary>
public class SwordOfHearthAndHomeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SwordOfHearthAndHome_Identity()
    {
        var c = SwordOfHearthAndHomeFactory.Create(_alice);

        c.Name.Should().Be("Sword of Hearth and Home");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfHearthAndHome_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of Hearth and Home", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sword of Hearth and Home");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void SwordOfHearthAndHome_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfHearthAndHomeFactory.Create(_alice);

        var equip = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2);
    }

    [Fact]
    public void SwordOfHearthAndHome_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfHearthAndHomeFactory.Create(_alice, svc, triggers: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(4);
    }

    [Fact]
    public void SwordOfHearthAndHome_HasProtectionFromGreenAndWhite_Markers()
    {
        var sword = SwordOfHearthAndHomeFactory.Create(_alice);

        var qualities = sword.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "green", "white" });

        Protection.HasProtectionFromColor(sword, ManaColor.Green).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.White).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Red).Should().BeFalse();
        Protection.HasProtectionFromColor(sword, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(sword, ManaColor.Black).Should().BeFalse();
    }

    [Fact]
    public void SwordOfHearthAndHome_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfHearthAndHomeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2)).Should().BeTrue();

        var blocker = new Creature("Blocker", "1G", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, blocker, 2)).Should().BeFalse(
            "printed text gates on 'to a player'");
    }

    [Fact]
    public void SwordOfHearthAndHome_OnCombatDamage_ExilesAndReturnsTargetCreature_AndTutorsBasic()
    {
        // Equipped Bear is Alice's; the target is a separate creature
        // Alice controls.
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfHearthAndHomeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var blink = new Creature("Blink Target", "1W", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(blink);

        // Seed a basic land into Alice's library so the tutor resolves.
        var plains = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains })
        {
            Owner = _alice,
        };
        plains.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(plains);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { blink },
        });

        foreach (var effect in trigger.Effects) effect.Execute();

        // Blink target is back on Alice's battlefield (owner's control).
        blink.Zone.Should().Be(ZoneType.Battlefield,
            "exile-then-return puts the chosen creature back on the battlefield (CR 701.20)");
        blink.Controller.Should().BeSameAs(_alice,
            "returned card enters under its owner's control");
        _alice.Zones.Battlefield.GetCards().Should().Contain(blink);
        _alice.Zones.Exile.GetCards().Should().NotContain(blink,
            "exile is a transient step in the flicker — the card has already returned");

        // Basic land tutored to battlefield tapped, library shuffled
        // (search succeeded so the card moved out of the library).
        _alice.Zones.Library.GetCards().Should().NotContain(plains);
        _alice.Zones.Battlefield.GetCards().Should().Contain(plains);
        plains.IsTapped.Should().BeTrue(
            "Sword of Hearth and Home puts the basic onto the battlefield tapped (CR 305.4)");
    }

    [Fact]
    public void SwordOfHearthAndHome_OnCombatDamage_TutorsBasic_EvenWhenTargetIsAbsent()
    {
        // Pre-supplied empty target list → exile/return half no-ops,
        // but the tutor rider still resolves (CR 608.2b — do as much as
        // possible; the tutor is not the targeted half).
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfHearthAndHomeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest })
        {
            Owner = _alice,
        };
        forest.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(forest);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        // No SetChosenTargets — exile/return half should no-op.

        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.IsTapped.Should().BeTrue();
    }
}
