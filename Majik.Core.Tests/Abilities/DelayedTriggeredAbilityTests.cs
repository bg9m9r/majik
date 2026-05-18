using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

public class DelayedTriggeredAbilityTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _manager;
    private readonly Player _alice = new("Alice", 20);

    public DelayedTriggeredAbilityTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _manager = new TriggerManager(_stack, _bus);
    }

    [Fact]
    public void DelayedTrigger_FiresOnce_ThenUnregisters()
    {
        var card = new Instant("Source", "1") { Owner = _alice };
        var fires = 0;
        var ability = new DelayedTriggeredAbility(
            card, _alice,
            Triggers.OnCardDrawnByPlayer(_alice),
            effects: new IEffect[] { new Effect("count", () => fires++) });
        _manager.RegisterDelayed(ability);

        // First draw — fires
        _bus.Publish(new CardDrawnEvent(card, _alice));
        _manager.PendingCount.Should().Be(1);
        _manager.PutPendingTriggersOnStack(_alice);
        _stack.Pop()!.Resolve();
        fires.Should().Be(1);
        _manager.IsRegistered(ability).Should().BeFalse();

        // Second draw — must NOT fire (ability auto-unregistered)
        _bus.Publish(new CardDrawnEvent(card, _alice));
        _manager.PendingCount.Should().Be(0);
        fires.Should().Be(1);
    }

    [Fact]
    public void DelayedTrigger_HasNoZoneRestriction_ByDefault()
    {
        var card = new Instant("Source", "1") { Owner = _alice, Zone = ZoneType.Graveyard };
        var ability = new DelayedTriggeredAbility(
            card, _alice,
            Triggers.OnCardDrawnByPlayer(_alice));

        ability.ActiveZones.Should().Contain(ZoneType.Graveyard,
            because: "delayed triggers fire regardless of source zone (Rule 603.7d)");
    }

    [Fact]
    public void RegisterDelayed_Null_Throws()
    {
        _manager.Invoking(m => m.RegisterDelayed(null!))
            .Should().Throw<ArgumentNullException>();
    }
}
