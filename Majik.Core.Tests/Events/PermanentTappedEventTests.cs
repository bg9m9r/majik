using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// Tests the CR 701.21 tap event: <see cref="Permanent.Tap(Player?)"/> now
/// publishes a <see cref="PermanentTappedEvent"/> via the ambient
/// <see cref="EventBusRegistry"/>, and
/// <see cref="Triggers.OnYouTapCreatureAnOpponentControls"/> matches it for
/// the "whenever you tap an untapped creature an opponent controls" hook
/// (Solitary Sanctuary). Closes the
/// <c>tap-event-and-whenever-you-tap-trigger</c> deferral.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class PermanentTappedEventTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public PermanentTappedEventTests()
    {
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(_bus);
    }

    public void Dispose()
    {
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    private Creature MakeCreature(Player controller)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Tap_PublishesPermanentTappedEvent_OnAmbientBus()
    {
        var creature = MakeCreature(_bob);
        PermanentTappedEvent? seen = null;
        _bus.Subscribe<PermanentTappedEvent>(e => seen = e);

        creature.Tap(causedBy: _alice);

        seen.Should().NotBeNull();
        seen!.Permanent.Should().BeSameAs(creature);
        seen.CausedBy.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Tap_FiresOncePerRealTap()
    {
        var creature = MakeCreature(_bob);
        var count = 0;
        _bus.Subscribe<PermanentTappedEvent>(_ => count++);

        creature.Tap();
        count.Should().Be(1);
    }

    [Fact]
    public void Untap_DoesNotPublishTappedEvent()
    {
        var creature = MakeCreature(_bob);
        creature.Tap();
        var countAfterUntap = 0;
        _bus.Subscribe<PermanentTappedEvent>(_ => countAfterUntap++);

        creature.Untap();

        countAfterUntap.Should().Be(0);
    }

    [Fact]
    public void YouTapTrigger_Fires_WhenYouTapOpponentsCreature()
    {
        var oppCreature = MakeCreature(_bob);
        var condition = Triggers.OnYouTapCreatureAnOpponentControls(_alice);

        var evt = new PermanentTappedEvent(oppCreature, causedBy: _alice);
        condition.Matches(evt, null!).Should().BeTrue();
    }

    [Fact]
    public void YouTapTrigger_DoesNotFire_WhenYouTapYourOwnCreature()
    {
        var ownCreature = MakeCreature(_alice);
        var condition = Triggers.OnYouTapCreatureAnOpponentControls(_alice);

        var evt = new PermanentTappedEvent(ownCreature, causedBy: _alice);
        condition.Matches(evt, null!).Should().BeFalse();
    }

    [Fact]
    public void YouTapTrigger_DoesNotFire_WhenTapperIsNotYou()
    {
        var oppCreature = MakeCreature(_bob);
        var condition = Triggers.OnYouTapCreatureAnOpponentControls(_alice);

        // Bob taps his own creature — Alice's "whenever YOU tap" must not fire.
        var evt = new PermanentTappedEvent(oppCreature, causedBy: _bob);
        condition.Matches(evt, null!).Should().BeFalse();
    }

    [Fact]
    public void YouTapTrigger_DoesNotFire_WithoutAttributedTapper()
    {
        var oppCreature = MakeCreature(_bob);
        var condition = Triggers.OnYouTapCreatureAnOpponentControls(_alice);

        // CR 603.2 — "you tap" requires a known "you"; an unattributed tap
        // (e.g. an untap-step-adjacent engine tap) does not fire it.
        var evt = new PermanentTappedEvent(oppCreature, causedBy: null);
        condition.Matches(evt, null!).Should().BeFalse();
    }

    [Fact]
    public void YouTapTrigger_DoesNotFire_OnNonCreatureTap()
    {
        var land = new Land("Forest");
        land.SetOwner(_bob);
        land.SetController(_bob);
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var condition = Triggers.OnYouTapCreatureAnOpponentControls(_alice);
        var evt = new PermanentTappedEvent(land, causedBy: _alice);
        condition.Matches(evt, null!).Should().BeFalse();
    }
}
