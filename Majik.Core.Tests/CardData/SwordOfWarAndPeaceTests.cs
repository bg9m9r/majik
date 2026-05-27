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
/// Unit tests for <see cref="SwordOfWarAndPeaceFactory"/> (New Phyrexia,
/// {3}).
///
/// Covers:
/// - Identity, dispatch.
/// - Equip {2} ability shape.
/// - Static +2/+2 via runtime overload + ContinuousEffectsService.
/// - Protection markers: "white" + "red".
/// - Combat-damage-to-a-player trigger condition.
/// - Combat-damage trigger resolution: damage to damaged player = their
///   hand size; controller gains 1 life per card in controller's hand.
/// </summary>
public class SwordOfWarAndPeaceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SwordOfWarAndPeace_Identity()
    {
        var c = SwordOfWarAndPeaceFactory.Create(_alice);

        c.Name.Should().Be("Sword of War and Peace");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfWarAndPeace_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of War and Peace", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sword of War and Peace");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void SwordOfWarAndPeace_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfWarAndPeaceFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2);
    }

    [Fact]
    public void SwordOfWarAndPeace_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfWarAndPeaceFactory.Create(_alice, svc, triggers: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(4);
    }

    [Fact]
    public void SwordOfWarAndPeace_HasProtectionFromWhiteAndRed_Markers()
    {
        var sword = SwordOfWarAndPeaceFactory.Create(_alice);

        var qualities = sword.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "white", "red" });

        Protection.HasProtectionFromColor(sword, ManaColor.White).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Red).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Blue).Should().BeFalse();
    }

    [Fact]
    public void SwordOfWarAndPeace_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
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
        var sword = SwordOfWarAndPeaceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue();
        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse();
        var dummy = new Creature("Dummy", "1G", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, dummy, 2))
            .Should().BeFalse();
    }

    [Fact]
    public void SwordOfWarAndPeace_CombatTrigger_DealsBobHandSize_AndGainsAliceHandSize()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfWarAndPeaceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        // Bob has 4 cards in hand → 4 damage. Alice has 3 cards → +3 life.
        for (int i = 0; i < 4; i++)
        {
            var c = new Creature($"BobCard{i}", "1G", 1, 1) { Owner = _bob };
            _bob.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }
        for (int i = 0; i < 3; i++)
        {
            var c = new Creature($"AliceCard{i}", "1G", 1, 1) { Owner = _alice };
            _alice.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue();

        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);

        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(16,
            "Sword deals 4 damage = Bob's hand size");
        _alice.LifeTotal.Should().Be(23,
            "Alice gains 3 life = her hand size");
    }

    [Fact]
    public void SwordOfWarAndPeace_CombatTrigger_EmptyHands_NoChange()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfWarAndPeaceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue();

        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(20, "Bob has 0 cards → 0 damage");
        _alice.LifeTotal.Should().Be(20, "Alice has 0 cards → 0 life gain");
    }
}
