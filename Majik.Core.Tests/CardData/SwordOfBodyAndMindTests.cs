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
/// Unit tests for <see cref="SwordOfBodyAndMindFactory"/> (Scars of
/// Mirrodin, {3}).
///
/// Covers:
/// - Identity, dispatch.
/// - Equip {2} ability shape.
/// - Static +2/+2 via runtime overload + ContinuousEffectsService.
/// - Protection markers: "green" + "blue".
/// - Combat-damage-to-a-player trigger condition.
/// - Combat-damage trigger resolution: 2/2 green Wolf token created +
///   damaged player mills 10.
/// </summary>
public class SwordOfBodyAndMindTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SwordOfBodyAndMind_Identity()
    {
        var c = SwordOfBodyAndMindFactory.Create(_alice);

        c.Name.Should().Be("Sword of Body and Mind");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfBodyAndMind_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of Body and Mind", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sword of Body and Mind");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void SwordOfBodyAndMind_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfBodyAndMindFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2);
    }

    [Fact]
    public void SwordOfBodyAndMind_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfBodyAndMindFactory.Create(_alice, svc, triggers: null, zones: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(4);
    }

    [Fact]
    public void SwordOfBodyAndMind_HasProtectionFromGreenAndBlue_Markers()
    {
        var sword = SwordOfBodyAndMindFactory.Create(_alice);

        var qualities = sword.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "green", "blue" });

        Protection.HasProtectionFromColor(sword, ManaColor.Green).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Blue).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.White).Should().BeFalse();
    }

    [Fact]
    public void SwordOfBodyAndMind_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
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
        var sword = SwordOfBodyAndMindFactory.Create(_alice);
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
    public void SwordOfBodyAndMind_CombatTrigger_CreatesWolfToken_AndMillsTen()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfBodyAndMindFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        // Seed Bob's library with 15 cards so 10 can be milled.
        for (int i = 0; i < 15; i++)
        {
            var c = new Creature($"Filler{i}", "1G", 1, 1) { Owner = _bob };
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        // Fire the condition closure so the captured damaged player is set.
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue();

        var beforeWolves = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.HasSubtype(CardSubtype.Wolf));

        foreach (var effect in trigger.Effects) effect.Execute();

        var wolves = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Wolf))
            .ToList();
        wolves.Should().HaveCount(beforeWolves + 1,
            "a 2/2 green Wolf token is created");
        var wolf = wolves.Last();
        wolf.BasePower.Should().Be(2);
        wolf.BaseToughness.Should().Be(2);
        wolf.IsToken.Should().BeTrue();
        wolf.Controller.Should().BeSameAs(_alice);

        _bob.Zones.Graveyard.GetCards().Should().HaveCount(10,
            "damaged player mills the top 10 cards of their library");
        _bob.Zones.Library.GetCards().Should().HaveCount(5);
    }
}
