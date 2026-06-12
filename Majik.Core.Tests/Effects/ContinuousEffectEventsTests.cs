using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 613 — the active-effects set publishes ContinuousEffectAdded /
/// ContinuousEffectRemoved through its wired <see cref="IEventBus"/> so the
/// portal action log can record a layer-effect entering / leaving the game.
/// One add per Register; one remove per removal funnel (Unregister, Prune,
/// ExpireEndOfTurn) — never double-published.
/// </summary>
public class ContinuousEffectEventsTests
{
    [Fact]
    public void Register_publishes_ContinuousEffectAddedEvent()
    {
        var bus = new RecordingBus();
        var svc = new ContinuousEffectsService(bus);
        var effect = NewDummyEffect();

        svc.Register(effect);

        var added = bus.Published.OfType<ContinuousEffectAddedEvent>().Should().ContainSingle().Subject;
        added.Effect.Should().BeSameAs(effect);
        bus.Published.OfType<ContinuousEffectRemovedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Unregister_publishes_ContinuousEffectRemovedEvent()
    {
        var bus = new RecordingBus();
        var svc = new ContinuousEffectsService(bus);
        var effect = NewDummyEffect();
        svc.Register(effect);
        bus.Clear();

        svc.Unregister(effect);

        var removed = bus.Published.OfType<ContinuousEffectRemovedEvent>().Should().ContainSingle().Subject;
        removed.Effect.Should().BeSameAs(effect);
    }

    [Fact]
    public void Unregister_of_unknown_effect_publishes_nothing()
    {
        var bus = new RecordingBus();
        var svc = new ContinuousEffectsService(bus);
        var effect = NewDummyEffect();

        // Never registered → nothing was removed → no remove event.
        svc.Unregister(effect);

        bus.Published.OfType<ContinuousEffectRemovedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Prune_publishes_one_ContinuousEffectRemovedEvent_per_dropped_effect()
    {
        var bus = new RecordingBus();
        var svc = new ContinuousEffectsService(bus);
        var inactive = NewDummyEffect(active: false);
        var active = NewDummyEffect(active: true);
        svc.Register(inactive);
        svc.Register(active);
        bus.Clear();

        svc.Prune();

        var removed = bus.Published.OfType<ContinuousEffectRemovedEvent>().ToList();
        removed.Should().ContainSingle();
        removed[0].Effect.Should().BeSameAs(inactive);
    }

    [Fact]
    public void ExpireEndOfTurn_publishes_one_ContinuousEffectRemovedEvent_per_expiring_effect()
    {
        var bus = new RecordingBus();
        var svc = new ContinuousEffectsService(bus);
        var eot = NewDummyEffect(expiresEot: true);
        var permanentEffect = NewDummyEffect();
        svc.Register(eot);
        svc.Register(permanentEffect);
        bus.Clear();

        svc.ExpireEndOfTurn();

        var removed = bus.Published.OfType<ContinuousEffectRemovedEvent>().ToList();
        removed.Should().ContainSingle();
        removed[0].Effect.Should().BeSameAs(eot);
    }

    [Fact]
    public void No_event_bus_does_not_throw()
    {
        // The bus-less ctor (sim / unit construction) must stay silent, not crash.
        var svc = new ContinuousEffectsService();
        var effect = NewDummyEffect();

        var register = () => svc.Register(effect);
        var unregister = () => svc.Unregister(effect);
        register.Should().NotThrow();
        unregister.Should().NotThrow();
    }

    private static DummyEffect NewDummyEffect(bool active = true, bool expiresEot = false)
    {
        var alice = new Player("Alice");
        var source = new Creature("Goblin Chieftain", "1RR", 2, 2)
        {
            Owner = alice,
            Controller = alice,
            Zone = ZoneType.Battlefield,
        };
        return new DummyEffect(source, active, expiresEot);
    }

    /// <summary>Minimal concrete effect: a no-op pump anchored to a source
    /// permanent, with configurable active / end-of-turn lifecycle so the
    /// removal-funnel tests can drive Prune / ExpireEndOfTurn.</summary>
    private sealed class DummyEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly bool _active;
        private readonly bool _expiresEot;

        public DummyEffect(Creature source, bool active, bool expiresEot)
        {
            _source = source;
            _active = active;
            _expiresEot = expiresEot;
        }

        public override Layer Layer => Layer.PT_Modify;
        public override Permanent? Source => _source;
        public override bool IsActive() => _active;
        public override bool ExpiresAtEndOfTurn => _expiresEot;
        public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _source);
        public override void Apply(CreatureCharacteristics chars) { /* no-op */ }
    }

    /// <summary>Records every published event via SubscribeAll (the production
    /// publish path), so it captures events published through the IEventBus
    /// reference regardless of static dispatch.</summary>
    private sealed class RecordingBus : EventBus
    {
        private readonly List<GameEvent> _published = new();
        public IReadOnlyList<GameEvent> Published => _published;
        public void Clear() => _published.Clear();

        public RecordingBus()
        {
            SubscribeAll(_published.Add);
        }
    }
}
