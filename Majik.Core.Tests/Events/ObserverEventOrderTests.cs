using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// An observer (the client bridge) sees events in publication order: a land
/// that enters tapped with an ETB trigger (Thundering Falls) is moved, then its
/// trigger triggers (CR 603.2, CR 603.6a). Before, the trigger manager
/// published the trigger from inside the move's delivery, so observers saw
/// the trigger first.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class ObserverEventOrderTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();

    public ObserverEventOrderTests()
    {
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(_bus);
    }

    public void Dispose()
    {
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    [Fact]
    public void ALandEnteringTappedWithAnEtbTrigger_IsSeenMovedThenTriggered_AndNeverTapped()
    {
        var replacements = new ReplacementBus();
        var zones = new ZoneService(_bus, replacements);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var land = new Land("Falls");
        land.SetOwner(_alice); land.SetController(_alice);
        land.AddAbility(new TriggeredAbility(land, _alice, Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { new Effect("surveil 1", () => { }) },
            activeZones: new[] { ZoneType.Battlefield }));
        replacements.Register(new EntersTappedReplacement(land));
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        triggers.BindCard(land);
        var seen = new List<string>();
        ((IEventBus)_bus).SubscribeObserver(e => seen.Add(e.GetType().Name));

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        seen.Should().ContainInOrder(nameof(CardMovedEvent), nameof(TriggeredAbilityTriggeredEvent));
        seen.Should().NotContain(nameof(PermanentTappedEvent));
    }

    [Fact]
    public void ANestedPublish_ReachesObserversAfterTheOuterEvent()
    {
        var seen = new List<string>();
        _bus.Subscribe<CardDrawnEvent>(_ => _bus.Publish(new GameStartedEvent()));
        ((IEventBus)_bus).SubscribeObserver(e => seen.Add(e.GetType().Name));
        var card = new Instant("X", "{R}");

        _bus.Publish(new CardDrawnEvent(card, _alice));

        seen.Should().Equal(nameof(CardDrawnEvent), nameof(GameStartedEvent));
    }
}