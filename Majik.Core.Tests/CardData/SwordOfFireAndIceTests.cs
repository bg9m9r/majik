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
/// Unit tests for <see cref="SwordOfFireAndIceFactory"/> (Darksteel, {3}).
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch + Equipment shape (Artifact + Equipment subtype).
/// - Equip activated ability shape: {2} mana cost.
/// - Static +2/+2 effect: equipped 2/2 Bear becomes 4/4.
/// - Protection markers: "red" + "blue" ProtectionAbility instances present;
///   <see cref="Protection.HasProtectionFromColor"/> answers true for both.
/// - Combat-damage-to-a-player trigger: condition gates on equipped creature
///   + non-null TargetPlayer; resolution deals 2 damage to chosen target +
///   draws a card.
/// </summary>
public class SwordOfFireAndIceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFireAndIce_Identity()
    {
        var c = SwordOfFireAndIceFactory.Create(_alice);

        c.Name.Should().Be("Sword of Fire and Ice");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Sword of Fire and Ice is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfFireAndIce_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of Fire and Ice", _alice);

        c.Should().BeOfType<Artifact>("Sword of Fire and Ice is an Artifact");
        c.Name.Should().Be("Sword of Fire and Ice");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage-to-a-player trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Equip {2} is wired");
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2,
            "protection from red + blue markers ride on the equipment");
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFireAndIce_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfFireAndIceFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effect — +2/+2
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFireAndIce_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfFireAndIceFactory.Create(_alice, svc, triggers: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2 power from Sword of Fire and Ice");
        bear.GetToughness().Should().Be(4, "+2 toughness from Sword of Fire and Ice");
    }

    // -----------------------------------------------------------------------
    // Protection markers
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFireAndIce_HasProtectionFromRedAndBlue_Markers()
    {
        var sword = SwordOfFireAndIceFactory.Create(_alice);

        var qualities = sword.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "red", "blue" },
            "Sword of Fire and Ice carries protection-from-red + protection-from-blue markers");

        // Protection helper resolves the markers off the equipment card itself.
        Protection.HasProtectionFromColor(sword, ManaColor.Red).Should().BeTrue(
            "the 'red' marker is visible to Protection helpers");
        Protection.HasProtectionFromColor(sword, ManaColor.Blue).Should().BeTrue(
            "the 'blue' marker is visible to Protection helpers");
        Protection.HasProtectionFromColor(sword, ManaColor.White).Should().BeFalse(
            "no protection-from-white marker is attached");
    }

    // -----------------------------------------------------------------------
    // Combat-damage-to-a-player trigger — condition gating
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFireAndIce_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
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
        var sword = SwordOfFireAndIceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();

        // Equipped Bear damages a player → matches.
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue("equipped creature dealt combat damage to a player (CR 510)");

        // A different (unequipped) creature damages a player → does not match.
        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse("trigger fires only for the equipped creature, not any creature");

        // Equipped Bear damages a creature (not a player) → does not match.
        var dummy = new Creature("Dummy", "1G", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, dummy, 2))
            .Should().BeFalse("printed text gates on 'to a player'");
    }

    // -----------------------------------------------------------------------
    // Combat-damage trigger — resolution effect (2 damage + draw 1)
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFireAndIce_CombatTrigger_DealsTwoToPlayer_AndDrawsOne()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfFireAndIceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        // Seed library so the draw resolves.
        var top = new Creature("Top", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Pre-populate the chosen target (Bob the player) on the trigger.
        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _bob.LifeTotal.Should().Be(20);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        // Resolve the effect.
        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(18,
            "Sword of Fire and Ice deals 2 damage to the chosen target (Bob)");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "the paired draw resolves alongside the damage");
        _alice.Zones.Hand.GetCards().Single().Should().BeSameAs(top,
            "top card was drawn");
    }
}
