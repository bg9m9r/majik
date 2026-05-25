using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Stinkweed Imp (Ravnica: City of Guilds, {1}{B}).
///
/// Covers:
///   - Card identity (name, mana cost, 1/2, Imp creature subtype).
///   - NamedCardFactory dispatch.
///   - Flying keyword marker (CR 702.9).
///   - Combat-damage-to-a-creature trigger structure (active on
///     battlefield) — fires on creature target, not on player target.
///   - Mechanic: damaged creature is moved Battlefield → Graveyard.
///   - Dredge 5 keyword marker (CR 702.52) with Arg = 5.
/// </summary>
public class StinkweedImpTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void StinkweedImp_Is_Imp_1_2_At_1B()
    {
        var imp = StinkweedImpFactory.Create(_alice);

        imp.Name.Should().Be("Stinkweed Imp");
        imp.ManaCost.Should().Be("{1}{B}");
        imp.BasePower.Should().Be(1);
        imp.BaseToughness.Should().Be(2);
        imp.HasType(CardType.Creature).Should().BeTrue();
        imp.HasSubtype(CardSubtype.Imp).Should().BeTrue();
        imp.Owner.Should().BeSameAs(_alice);
        imp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StinkweedImp()
    {
        var card = NamedCardFactory.Create("Stinkweed Imp", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Stinkweed Imp");
        card.HasSubtype(CardSubtype.Imp).Should().BeTrue();
    }

    [Fact]
    public void StinkweedImp_HasFlyingMarker()
    {
        var imp = StinkweedImpFactory.Create(_alice);

        imp.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying");
    }

    [Fact]
    public void StinkweedImp_HasDredge5Marker()
    {
        var imp = StinkweedImpFactory.Create(_alice);

        imp.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Dredge")
            .Which.Arg.Should().Be(5);
    }

    [Fact]
    public void StinkweedImp_HasCombatDamageTrigger_ActiveOnBattlefieldOnly()
    {
        var imp = StinkweedImpFactory.Create(_alice);

        var triggers = imp.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].ActiveZones.Should().ContainSingle(z => z == ZoneType.Battlefield);
    }

    [Fact]
    public void StinkweedImp_CombatDamageToCreature_DestroysCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var imp = StinkweedImpFactory.Create(_alice, triggers, replacements: null);
        imp.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(imp);

        var victim = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        victim.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(victim);

        bus.Publish(new CombatDamageDealtEvent(imp, victim, amount: 1));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        victim.Zone.Should().Be(ZoneType.Graveyard,
            "Stinkweed Imp's combat damage trigger destroys the damaged creature");
        _bob.Zones.Graveyard.GetCards().Should().Contain(victim);
    }

    [Fact]
    public void StinkweedImp_CombatDamageToPlayer_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var imp = StinkweedImpFactory.Create(_alice, triggers, replacements: null);
        imp.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(imp);

        bus.Publish(new CombatDamageDealtEvent(imp, _bob, amount: 1));

        triggers.PendingCount.Should().Be(0,
            "trigger gates on 'to a creature', not 'to a player'");
    }
}
