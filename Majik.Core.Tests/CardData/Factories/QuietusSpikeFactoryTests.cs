using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Quietus Spike (Zendikar, Artifact — Equipment {3}).
///   "Whenever equipped creature deals combat damage to a player, that
///    player loses half their life, rounded up."
///   "Equip {3}."
///
/// Validates:
///   * Card identity (Artifact — Equipment at {3}) + dispatcher entry.
///   * Equip {3} activated ability shape.
///   * Combat trigger fires only when the equipped creature deals
///     combat damage to a player (not to a creature, not from an
///     unattached state).
///   * Resolution drains ceil(life / 2) from the target player.
/// </summary>
[Trait("Color", "C")]
public class QuietusSpikeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void QuietusSpike_IsArtifactEquipment_AtCost3()
    {
        var card = QuietusSpikeFactory.Create(_alice);

        card.Name.Should().Be("Quietus Spike");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        card.ManaCost.Should().Be("{3}");
        card.Owner.Should().BeSameAs(_alice);
    }
    [Fact]
    public void QuietusSpike_HasEquipThreeActivatedAbility()
    {
        var card = QuietusSpikeFactory.Create(_alice);

        var equip = card.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.IsSorcerySpeed.Should().BeTrue("Equip is sorcery speed (CR 702.6b)");
        equip.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(3,
            "printed equip cost is {3}");
    }

    [Fact]
    public void QuietusSpike_EquippedCreature_DealsCombatDamageToPlayer_DrainsHalfRoundedUp()
    {
        var spike = QuietusSpikeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spike);
        spike.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        spike.AttachTo(bear);
        spike.AttachedTo.Should().BeSameAs(bear);

        // Bob is at 20 life. Bear deals 2 combat damage. Trigger should
        // drain ceil(20 / 2) = 10 life off Bob via the trigger.
        var trigger = spike.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CombatDamageDealtEvent(bear, _bob, amount: 2);
        trigger.IsTriggered(ev).Should().BeTrue();

        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(10,
            "Bob loses ceil(20 / 2) = 10 life (printed 'half rounded up')");
    }

    [Fact]
    public void QuietusSpike_OddLife_RoundsUp()
    {
        var spike = QuietusSpikeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spike);
        spike.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        spike.AttachTo(bear);

        // Bob at 15. Half rounded up = 8. So Bob ends at 7.
        _bob.LoseLife(5);
        _bob.LifeTotal.Should().Be(15);

        var trigger = spike.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, amount: 2))
            .Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(7,
            "Bob at 15 loses ceil(15 / 2) = 8 → ends at 7 (printed 'rounded up')");
    }

    [Fact]
    public void QuietusSpike_DamageToCreature_DoesNotTrigger()
    {
        var spike = QuietusSpikeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spike);
        spike.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        var blocker = new Creature("Wall", "{1}", 0, 4)
        {
            Owner = _bob,
            Controller = _bob,
        };
        spike.AttachTo(bear);

        var trigger = spike.Abilities.OfType<TriggeredAbility>().Single();
        // Combat damage to a creature, not a player.
        var ev = new CombatDamageDealtEvent(bear, (ICard?)blocker, amount: 2);
        trigger.IsTriggered(ev).Should().BeFalse(
            "printed text gates on 'damage to a player' — creature damage doesn't fire");
    }

    [Fact]
    public void QuietusSpike_Unattached_DoesNotTrigger()
    {
        var spike = QuietusSpikeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spike);
        spike.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        // Note: spike is NOT attached.

        var trigger = spike.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CombatDamageDealtEvent(bear, _bob, amount: 2);
        trigger.IsTriggered(ev).Should().BeFalse(
            "without an equipped creature the trigger gate fails (AttachedTo is null)");
    }
}
