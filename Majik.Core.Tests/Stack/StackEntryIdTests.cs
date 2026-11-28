using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Stack;

/// <summary>
/// CR 603.3 — each firing of a triggered ability is its own stack object. The
/// engine pushes the registered ability object once per firing; the stack gives
/// each copy its own entry id (StackEntryIds.Repeat for repeats) and the add /
/// resolve events carry it. Resolution behaviour is unchanged.
/// </summary>
public class StackEntryIdTests
{
    [Fact]
    public void ARepeatPushOfOneTrigger_GetsItsOwnEntryId_AndTheEventsCarryIt()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var alice = new Player("Alice", 20);
        var source = new Creature("Writhing Chrysalis", "{2}{B}{G}", 2, 3);
        source.SetOwner(alice); source.SetController(alice);
        alice.Zones.Battlefield.AddCard(source); source.SetZone(ZoneType.Battlefield);
        var resolved = 0;
        var trigger = new TriggeredAbility(source, alice, Triggers.OnEnterBattlefieldSelf(source),
            effects: new IEffect[] { new Effect("Create a 0/1 Eldrazi Spawn.", () => resolved++) });
        var added = new List<Guid?>();
        var resolvedIds = new List<Guid?>();
        bus.Subscribe<StackObjectAddedEvent>(e => added.Add(e.EntryId));
        bus.Subscribe<StackObjectResolvedEvent>(e => resolvedIds.Add(e.EntryId));

        stack.Push(trigger);
        stack.Push(trigger);

        var ids = stack.GetEntryIds();
        ids.Should().Equal(trigger.Id, StackEntryIds.Repeat(trigger.Id, 1));
        added.Should().Equal(ids.Cast<Guid?>());
        stack.TopEntryId.Should().Be(ids[1]);

        var resolver = new Majik.Core.Services.StackResolver(bus);
        resolver.ResolveTop(stack);
        resolver.ResolveTop(stack);
        resolvedIds.Should().Equal(ids[1], ids[0]);
        resolved.Should().Be(2, "both firings still resolve the shared ability");
        stack.GetEntryIds().Should().BeEmpty();
    }
}