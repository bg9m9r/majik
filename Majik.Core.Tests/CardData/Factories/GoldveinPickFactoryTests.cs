using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GoldveinPickFactory"/>.
///
/// Card: Goldvein Pick — Artifact — Equipment ({1}).
///   "Equipped creature gets +1/+1."
///   "Whenever equipped creature deals combat damage to a player, create a
///    Treasure token."
///   "Equip {1}"
///
/// The +1/+1 boost + Equip primitive + combat-damage-to-a-player trigger
/// mirror <see cref="SwordOfFireAndIceFactory"/>; the unique payoff is the
/// Treasure token (CR 111.10) emitted on resolution instead of damage + a
/// draw.
/// </summary>
[Trait("Color", "C")]
public class GoldveinPickFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GoldveinPick_Identity()
    {
        var c = GoldveinPickFactory.Create(_alice);

        c.Name.Should().Be("Goldvein Pick");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Goldvein Pick is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Goldvein Pick has a single combat-damage-to-a-player trigger");
    }

    [Fact]
    public void GoldveinPick_EquipAbility_HasGenericOneCost()
    {
        var c = GoldveinPickFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1, "Equip {1} is the printed activation cost");
    }

    [Fact]
    public void GoldveinPick_GrantsPlusOnePlusOne_WhileEquipped()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var pick = GoldveinPickFactory.Create(_alice, svc, triggers: null, zones: null);
        pick.Zone = ZoneType.Battlefield;

        // Before equip: base 2/2.
        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);

        pick.AttachTo(bear);

        bear.GetPower().Should().Be(3, "CR 613 Layer 7c — +1 power from Goldvein Pick");
        bear.GetToughness().Should().Be(3, "CR 613 Layer 7c — +1 toughness from Goldvein Pick");
    }

    [Fact]
    public void GoldveinPick_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var other = new Creature("Other", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var pick = GoldveinPickFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pick);
        pick.SetZone(ZoneType.Battlefield);
        pick.AttachTo(bear);

        var trigger = pick.Abilities.OfType<TriggeredAbility>().Single();

        // Equipped creature damages a player → matches.
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue("equipped creature dealt combat damage to a player (CR 510)");

        // A different (unequipped) creature damages a player → does not match.
        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse("trigger fires only for the equipped creature");

        // Equipped creature damages a creature (not a player) → does not match.
        var dummy = new Creature("Dummy", "{1}{G}", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, dummy, 2))
            .Should().BeFalse("printed text gates on 'to a player'");
    }

    [Fact]
    public void GoldveinPick_CombatTrigger_CreatesOneTreasure()
    {
        var zones = new ZoneService();
        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var pick = GoldveinPickFactory.Create(_alice, continuousEffects: null, triggers: null, zones: zones);
        _alice.Zones.Battlefield.AddCard(pick);
        pick.SetZone(ZoneType.Battlefield);
        pick.AttachTo(bear);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure))
            .Should().Be(0, "no Treasure exists before the trigger resolves");

        var trigger = pick.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // CR 111.10 — exactly one colourless Treasure artifact token enters
        // under the equipment controller.
        var treasures = _alice.Zones.Battlefield.GetCards()
            .Where(c => c.HasSubtype(CardSubtype.Treasure))
            .ToList();
        treasures.Should().HaveCount(1,
            "each combat-damage trigger creates a single Treasure token");
        treasures[0].HasType(CardType.Artifact).Should().BeTrue(
            "a Treasure token is an artifact");
    }
}
