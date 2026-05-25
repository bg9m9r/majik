using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Golgari Grave-Troll (Ravnica: City of Guilds, {3}{B}{G}).
///
/// Covers:
///   - Card identity (name, mana cost, base 0/0, Zombie Troll subtypes).
///   - NamedCardFactory dispatch.
///   - ETB trigger structure (active on battlefield, ETB condition).
///   - Mechanic: enters with +1/+1 counters equal to creature cards in
///     controller's graveyard at resolve time.
///   - Dredge 6 keyword marker (CR 702.52) with Arg = 6.
/// </summary>
public class GolgariGraveTrollTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GraveTroll_Is_ZombieTroll_0_0_At_3BG()
    {
        var troll = GolgariGraveTrollFactory.Create(_alice);

        troll.Name.Should().Be("Golgari Grave-Troll");
        troll.ManaCost.Should().Be("{3}{B}{G}");
        troll.BasePower.Should().Be(0);
        troll.BaseToughness.Should().Be(0);
        troll.HasType(CardType.Creature).Should().BeTrue();
        troll.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        troll.HasSubtype(CardSubtype.Troll).Should().BeTrue();
        troll.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GraveTroll()
    {
        var card = NamedCardFactory.Create("Golgari Grave-Troll", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Troll).Should().BeTrue();
    }

    [Fact]
    public void GraveTroll_HasDredge6Marker()
    {
        var troll = GolgariGraveTrollFactory.Create(_alice);

        troll.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Dredge")
            .Which.Arg.Should().Be(6);
    }

    [Fact]
    public void GraveTroll_HasEtbTrigger()
    {
        var troll = GolgariGraveTrollFactory.Create(_alice);

        var triggers = troll.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].ActiveZones.Should().ContainSingle(z => z == ZoneType.Battlefield);
    }

    [Fact]
    public void GraveTroll_EtbWithCounters_OneCounterPerCreatureCardInGraveyard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Stage 3 creature cards in Alice's graveyard.
        for (int i = 0; i < 3; i++)
        {
            var c = new Creature($"Bear-{i}", "{1}{G}", power: 2, toughness: 2);
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }
        // Add a non-creature card — should NOT contribute.
        var sorcery = new Sorcery("Test Sorcery", "{1}");
        sorcery.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(sorcery);
        sorcery.SetZone(ZoneType.Graveyard);

        var troll = GolgariGraveTrollFactory.Create(_alice, triggers, replacements: null);
        troll.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(troll);

        // Fire the ETB.
        bus.Publish(new CardMovedEvent(troll, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        troll.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "ETB body counts ONLY Creature cards in controller's graveyard");
    }

    [Fact]
    public void GraveTroll_EtbWithEmptyGraveyard_NoCountersAdded()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var troll = GolgariGraveTrollFactory.Create(_alice, triggers, replacements: null);
        troll.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(troll);

        bus.Publish(new CardMovedEvent(troll, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        troll.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no creatures in graveyard -> no counters; SBA will clean up the 0/0");
    }
}
