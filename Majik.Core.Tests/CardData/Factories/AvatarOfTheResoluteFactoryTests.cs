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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AvatarOfTheResoluteFactory"/>.
///
/// Card: Avatar of the Resolute — Creature — Avatar {G}{G} 3/2 (Magic Origins).
///   "Reach, trample
///    This creature enters with a +1/+1 counter on it for each other creature
///    you control with a +1/+1 counter on it."
///
/// Covers:
///   - Identity (name, {G}{G}, 3/2, Avatar, green) + NamedCardFactory dispatch.
///   - Reach + Trample keyword markers (CR 702.17 / CR 702.19).
///   - ETB trigger structure (single trigger, active on battlefield).
///   - ETB body counts ONLY other creatures you control bearing a +1/+1
///     counter; non-counter creatures and the Avatar itself do not contribute;
///     an opponent's countered creature does not contribute.
/// </summary>
public class AvatarOfTheResoluteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Avatar_Identity()
    {
        var c = AvatarOfTheResoluteFactory.Create(_alice);

        c.Name.Should().Be("Avatar of the Resolute");
        c.ManaCost.Should().Be("{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Avatar()
    {
        var card = NamedCardFactory.Create("Avatar of the Resolute", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Avatar of the Resolute");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        card.ManaCost.Should().Be("{G}{G}");
    }

    [Fact]
    public void Avatar_HasReachAndTrampleMarkers()
    {
        var c = AvatarOfTheResoluteFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Any(k => string.Equals(k.Keyword, "Reach", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Avatar of the Resolute has Reach (CR 702.17)");
        keywords.Any(k => string.Equals(k.Keyword, "Trample", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Avatar of the Resolute has Trample (CR 702.19)");
    }

    [Fact]
    public void Avatar_HasEtbTrigger()
    {
        var c = AvatarOfTheResoluteFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].ActiveZones.Should().ContainSingle(z => z == ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Dynamic enters-with-counters (CR 603.6a / CR 614.1d)
    // -----------------------------------------------------------------------

    private Creature StageCreature(Player controller, string name, int plusOneCounters)
    {
        var c = new Creature(name, "{1}{G}", power: 2, toughness: 2);
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        if (plusOneCounters > 0)
            c.Counters.Add(CounterType.PlusOnePlusOne, plusOneCounters);
        return c;
    }

    [Fact]
    public void Etb_CountsOnlyOtherCreaturesYouControlWithACounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Two of Alice's creatures HAVE a +1/+1 counter (contribute).
        StageCreature(_alice, "Counter-A", plusOneCounters: 1);
        StageCreature(_alice, "Counter-B", plusOneCounters: 3);
        // One of Alice's creatures has NO counter (does not contribute).
        StageCreature(_alice, "Vanilla", plusOneCounters: 0);
        // Bob controls a creature WITH a counter — not "you control", no contribution.
        StageCreature(_bob, "Opp-Counter", plusOneCounters: 2);

        var avatar = AvatarOfTheResoluteFactory.Create(_alice, triggers);
        avatar.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(avatar);

        // Fire the ETB.
        bus.Publish(new CardMovedEvent(avatar, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        avatar.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "exactly the two OTHER creatures Alice controls that bear a +1/+1 counter contribute");
    }

    [Fact]
    public void Etb_NoOtherCounteredCreatures_NoCountersAdded()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // A vanilla creature without a counter does not contribute.
        StageCreature(_alice, "Vanilla", plusOneCounters: 0);

        var avatar = AvatarOfTheResoluteFactory.Create(_alice, triggers);
        avatar.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(avatar);

        bus.Publish(new CardMovedEvent(avatar, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        avatar.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no OTHER creature you control bears a +1/+1 counter -> enters as a vanilla 3/2");
    }
}
