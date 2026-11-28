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
/// CR 614.1c / 110.5 — a permanent that enters tapped has that status as it
/// enters; it never "becomes tapped" (no tap happened). No tap event is
/// published, and "whenever ~ becomes tapped" does not trigger.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class EntersTappedIsNotATapTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;

    public EntersTappedIsNotATapTests()
    {
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(_bus);
        _zones = new ZoneService(_bus, _replacements);
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
    }

    public void Dispose()
    {
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    private Land PainfulLand()
    {
        // "Whenever this land becomes tapped, it deals 1 damage to you."
        var land = new Land("Painful Land");
        land.SetOwner(_alice); land.SetController(_alice);
        land.AddAbility(new TriggeredAbility(land, _alice, Triggers.OnThisBecomesTapped(land),
            effects: new IEffect[] { new Effect("1 damage to you", () => _alice.LoseLife(1)) },
            activeZones: new[] { ZoneType.Battlefield }));
        _replacements.Register(new EntersTappedReplacement(land));
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        _triggers.BindCard(land);
        return land;
    }

    [Fact]
    public void EnteringTapped_PublishesNoTapEvent()
    {
        var land = PainfulLand();
        var taps = new List<PermanentTappedEvent>();
        _bus.Subscribe<PermanentTappedEvent>(taps.Add);

        _zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        land.IsTapped.Should().BeTrue("CR 614.1c — it enters tapped");
        taps.Should().BeEmpty("entering tapped is not becoming tapped");
    }

    [Fact]
    public void EnteringTapped_DoesNotTriggerBecomesTapped_ButALaterTapDoes()
    {
        var land = PainfulLand();

        _zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);
        _triggers.PutPendingTriggersOnStack(_alice);

        _stack.IsEmpty.Should().BeTrue("no tap happened");
        _alice.LifeTotal.Should().Be(20);

        land.Untap();
        land.Tap(_alice);
        _triggers.PutPendingTriggersOnStack(_alice);
        _stack.IsEmpty.Should().BeFalse("a real tap still triggers it");
    }
}