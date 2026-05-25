using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// CR 701.42 — Tests for <see cref="SurveilEvent"/> publication via
/// <see cref="Fx.Surveil"/>, and for the
/// <see cref="Triggers.OnSurveil(Player)"/> trigger condition that
/// payoff cards (Ledger Shredder, Dimir Spybug, …) subscribe to.
///
/// Two surfaces under test:
///   - Fx.Surveil publishes SurveilEvent with the right player, N, and
///     pre-decision peeked cards on the supplied bus.
///   - A subscriber to SurveilEvent on the event bus fires for every
///     surveil action published by Fx.Surveil, regardless of source.
/// </summary>
public class SurveilEventTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    [Fact]
    public void FxSurveil_PublishesSurveilEvent_OnSuppliedBus_WithCorrectN_AndPeekedCards()
    {
        var bus = new EventBus();
        SurveilEvent? captured = null;
        bus.Subscribe<SurveilEvent>(e => captured = e);

        var top = NewCardInLibrary(_alice, "Top");
        var mid = NewCardInLibrary(_alice, "Mid");
        var bot = NewCardInLibrary(_alice, "Bot");

        // Surveil 2 — graveyard-bound = top, top-bound = mid.
        var decision = new SurveilAction.SurveilDecision(
            ToGraveyard: new[] { top },
            TopOrder: new[] { mid });

        Fx.Surveil(_alice, 2, decision, bus);

        captured.Should().NotBeNull();
        captured!.Player.Should().BeSameAs(_alice);
        captured.N.Should().Be(2);
        // Pre-decision peeked top-2 in library order: { top, mid }.
        captured.Cards.Should().Equal(new ICard[] { top, mid });

        // Library / graveyard reflect decision.
        _alice.Zones.Library.GetCards().Should().Equal(new[] { mid, bot });
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
    }

    [Fact]
    public void FxSurveil_NoBusRegistered_DoesNotThrow()
    {
        // EventBusRegistry empty by default per test; no explicit bus.
        EventBusRegistry.Clear();
        var top = NewCardInLibrary(_alice, "Top");

        var decision = new SurveilAction.SurveilDecision(
            ToGraveyard: new[] { top },
            TopOrder: Array.Empty<ICard>());

        var act = () => Fx.Surveil(_alice, 1, decision);
        act.Should().NotThrow();

        // Surveil still applied.
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
    }

    [Fact]
    public void FxSurveil_UsesEventBusRegistry_WhenNoExplicitBusSupplied()
    {
        var bus = new EventBus();
        SurveilEvent? captured = null;
        bus.Subscribe<SurveilEvent>(e => captured = e);

        EventBusRegistry.Clear();
        EventBusRegistry.Set(_alice, bus);
        try
        {
            var top = NewCardInLibrary(_alice, "Top");
            var decision = new SurveilAction.SurveilDecision(
                ToGraveyard: new[] { top },
                TopOrder: Array.Empty<ICard>());

            Fx.Surveil(_alice, 1, decision);

            captured.Should().NotBeNull();
            captured!.Player.Should().BeSameAs(_alice);
            captured.N.Should().Be(1);
        }
        finally
        {
            EventBusRegistry.Clear();
        }
    }

    [Fact]
    public void TriggersOnSurveil_FiresForMatchingPlayer_DoesNotFireForOther()
    {
        var bob = new Player("Bob", 20);
        var bus = new EventBus();

        int aliceFires = 0;
        int bobFires = 0;

        var aliceCond = Triggers.OnSurveil(_alice);
        var bobCond = Triggers.OnSurveil(bob);

        bus.SubscribeAll(e =>
        {
            if (aliceCond.Matches(e, null!)) aliceFires++;
            if (bobCond.Matches(e, null!)) bobFires++;
        });

        bus.Publish(new SurveilEvent(_alice, 1, Array.Empty<ICard>()));
        bus.Publish(new SurveilEvent(bob, 2, Array.Empty<ICard>()));
        bus.Publish(new SurveilEvent(_alice, 1, Array.Empty<ICard>()));

        aliceFires.Should().Be(2);
        bobFires.Should().Be(1);
    }

    [Fact]
    public void MultipleSubscribers_AllReceiveTheSameSurveilEvent()
    {
        var bus = new EventBus();
        int callsA = 0, callsB = 0;
        bus.Subscribe<SurveilEvent>(_ => callsA++);
        bus.Subscribe<SurveilEvent>(_ => callsB++);

        var top = NewCardInLibrary(_alice, "Top");
        var decision = new SurveilAction.SurveilDecision(
            ToGraveyard: new[] { top },
            TopOrder: Array.Empty<ICard>());

        Fx.Surveil(_alice, 1, decision, bus);

        callsA.Should().Be(1);
        callsB.Should().Be(1);
    }

    [Fact]
    public void LedgerShredderTrigger_FiresViaBus_AfterEachSurveil()
    {
        // Integration: register two SurveilEvent subscribers ("Ledger
        // Shredder" plus a sibling payoff). Both should fire once per
        // surveil performed by Fx.Surveil.
        var bus = new EventBus();
        int shredderFires = 0;
        int siblingFires = 0;

        var aliceCond = Triggers.OnSurveil(_alice);
        bus.Subscribe<SurveilEvent>(e =>
        {
            if (aliceCond.Matches(e, null!)) shredderFires++;
        });
        bus.Subscribe<SurveilEvent>(e =>
        {
            if (aliceCond.Matches(e, null!)) siblingFires++;
        });

        var c1 = NewCardInLibrary(_alice, "C1");
        var c2 = NewCardInLibrary(_alice, "C2");
        var c3 = NewCardInLibrary(_alice, "C3");

        // Two distinct surveils.
        Fx.Surveil(_alice, 1,
            new SurveilAction.SurveilDecision(
                ToGraveyard: new[] { c1 },
                TopOrder: Array.Empty<ICard>()),
            bus);
        Fx.Surveil(_alice, 1,
            new SurveilAction.SurveilDecision(
                ToGraveyard: new[] { c2 },
                TopOrder: Array.Empty<ICard>()),
            bus);

        shredderFires.Should().Be(2);
        siblingFires.Should().Be(2);

        // Graveyard reflects both surveils, library has C3 left.
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { c1, c2 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { c3 });
    }

    [Fact]
    public void FxSurveil_EmptyLibrary_PublishesEventWithEmptyCardsList()
    {
        var bus = new EventBus();
        SurveilEvent? captured = null;
        bus.Subscribe<SurveilEvent>(e => captured = e);

        // Empty library — surveil 1 still publishes (engineering choice
        // matching CR 701.42a's "attempted surveil").
        var decision = new SurveilAction.SurveilDecision(
            ToGraveyard: Array.Empty<ICard>(),
            TopOrder: Array.Empty<ICard>());

        Fx.Surveil(_alice, 1, decision, bus);

        captured.Should().NotBeNull();
        captured!.N.Should().Be(1);
        captured.Cards.Should().BeEmpty();
    }
}
