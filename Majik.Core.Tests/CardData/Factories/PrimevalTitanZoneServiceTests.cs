using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for tutor / fetch routing through <see cref="ZoneService"/>.
///
/// Until this slice, <see cref="PrimevalTitanFactory"/> (and friends —
/// <see cref="ScapeshiftFactory"/>, <see cref="FetchLandCycleFactory"/>,
/// the <c>SearchForTomorrow</c> path) moved tutored lands onto the
/// battlefield with raw <c>Zones.Library.RemoveCard</c> +
/// <c>Zones.Battlefield.AddCard</c>. That bypasses
/// <see cref="ZoneService.MoveCard"/>'s <see cref="CardMovedEvent"/>
/// publication and the <see cref="ReplacementBus"/> hook — so:
///
/// - Bounce-land ETB-bounce triggers never fired on the tutored land
///   (Primeval Titan → Simic Growth Chamber should bounce a land but
///   didn't because the bounce land's "When this land enters" trigger
///   never saw an event).
/// - Amulet of Vigor's "Whenever a permanent enters tapped under your
///   control, untap it" trigger never fired, so fetched bounce lands
///   stayed tapped through Amulet of Vigor.
/// - ETB-tapped replacement effects on shock lands / bounce lands never
///   applied because the <see cref="ReplacementBus"/> never saw a
///   <see cref="ZoneMoveIntent"/>.
///
/// The fix routes the tutor's Library → Battlefield move through
/// <see cref="ZoneServiceRegistry"/>-discovered <see cref="ZoneService"/>
/// when one is registered for the caster (which <see cref="Majik.Core.Game.GameDriver"/>
/// does at construction time).
/// </summary>
[Trait("Color", "C")]
public class PrimevalTitanZoneServiceTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly IEventBus _bus = new EventBus();

    public void Dispose()
    {
        // Clear the process-wide registry so other test fixtures don't see
        // a stale ZoneService. Mirrors AgentRegistry.Clear() + GameRandomRegistry.Clear()
        // teardown in fixture-using tests.
        ZoneServiceRegistry.Clear();
    }

    private static Land MakeBasicLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    // -----------------------------------------------------------------------
    // Bounce land — ETB bounce trigger fires on tutored arrival
    // -----------------------------------------------------------------------

    [Fact]
    public void PrimevalTitan_TutorsBounceLand_BounceTriggerGoesPending()
    {
        // Wire the live service graph the tutor closure depends on.
        var stack = new Majik.Core.Stack.Stack(_bus);
        var replacements = new ReplacementBus();
        var zones = new ZoneService(_bus, replacements);
        var triggers = new TriggerManager(stack, _bus);

        // Register the live ZoneService so the tutor closure looks it
        // up at execution time (mirrors GameDriver's setup).
        ZoneServiceRegistry.Set(_alice, zones);

        // Build the bounce land (Simic Growth Chamber) with triggers +
        // replacements wired so its "When this land enters, return a land
        // you control to its owner's hand" trigger goes pending when it
        // enters via ZoneService.MoveCard.
        var simic = BounceLandCycleFactory.Create(
            _alice,
            new[] { "Simic Growth Chamber", "G", "U" },
            zoneService: zones,
            eventBus: _bus,
            triggers: triggers,
            replacements: replacements);

        _alice.Zones.Library.AddCard(simic);
        simic.SetZone(ZoneType.Library);

        // A second land on the battlefield so the bounce trigger has a
        // legal target (its candidates filter excludes the bounce land
        // itself per the v1 factory's not-self rule).
        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        // Build Primeval Titan with a deterministic tutor selector that
        // picks the bounce land.
        var titan = PrimevalTitanFactory.Create(
            _alice, triggers: triggers,
            selector: _ => new ICard[] { simic });
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        // Fire the Primeval Titan ETB tutor — it should route the bounce
        // land's Library → Battlefield move through ZoneService.MoveCard,
        // which publishes CardMovedEvent → the bounce land's ETB trigger
        // condition matches → trigger queues on the manager.
        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        // CR 603.6a / CR 614 — the tutored bounce land sees its own ETB
        // event because the move ran through ZoneService.
        simic.Zone.Should().Be(ZoneType.Battlefield);
        triggers.PendingCount.Should().BeGreaterThan(0,
            "bounce-land ETB trigger should have queued on tutored arrival");
    }

    // -----------------------------------------------------------------------
    // Amulet of Vigor — untap trigger fires on tutored tapped arrival
    // -----------------------------------------------------------------------

    [Fact]
    public void PrimevalTitan_TutorsBounceLand_AmuletOfVigorUntapTriggerGoesPending()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var replacements = new ReplacementBus();
        var zones = new ZoneService(_bus, replacements);
        var triggers = new TriggerManager(stack, _bus);

        ZoneServiceRegistry.Set(_alice, zones);

        // Amulet of Vigor on the battlefield, trigger registered with the
        // live manager.
        var amulet = AmuletOfVigorFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(amulet);
        amulet.SetZone(ZoneType.Battlefield);

        // Simic Growth Chamber in the library — always-tapped via its
        // ConditionalEntersTappedReplacement (CR 614.1c).
        var simic = BounceLandCycleFactory.Create(
            _alice,
            new[] { "Simic Growth Chamber", "G", "U" },
            zoneService: zones,
            eventBus: _bus,
            triggers: triggers,
            replacements: replacements);
        _alice.Zones.Library.AddCard(simic);
        simic.SetZone(ZoneType.Library);

        // Secondary land so bounce trigger has a legal pick (mirrors first test).
        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var titan = PrimevalTitanFactory.Create(
            _alice, triggers: triggers,
            selector: _ => new ICard[] { simic });
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        // CR 614 — the bounce land's ETB-tapped replacement applied
        // during ZoneService.MoveCard; Permanent.Tap() was called inside
        // ZoneService BEFORE CardMovedEvent published, so Amulet's
        // condition (IsTapped + Controller match) saw the tapped state at
        // event-evaluation time.
        simic.Zone.Should().Be(ZoneType.Battlefield);
        simic.IsTapped.Should().BeTrue(
            "the bounce land's ETB-tapped replacement plus the post-move tap from " +
            "Primeval Titan both leave the bounce land tapped (double-tap = tapped)");

        // The pending queue includes Amulet of Vigor's untap trigger
        // alongside the bounce land's bounce trigger.
        triggers.PendingCount.Should().BeGreaterOrEqualTo(2,
            "Amulet of Vigor's untap trigger + bounce-land bounce trigger should both queue");
    }

    // -----------------------------------------------------------------------
    // CardMovedEvent publication — direct observation
    // -----------------------------------------------------------------------

    [Fact]
    public void PrimevalTitan_TutorRoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var zones = new ZoneService(_bus);
        ZoneServiceRegistry.Set(_alice, zones);

        var observed = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(e => observed.Add(e));

        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var titan = PrimevalTitanFactory.Create(
            _alice, triggers: null,
            selector: _ => new ICard[] { forest });
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        observed.Should().ContainSingle(e =>
            ReferenceEquals(e.Card, forest)
            && e.FromZone == ZoneType.Library
            && e.ToZone == ZoneType.Battlefield,
            "tutor routes through ZoneService → CardMovedEvent published for the moved land");

        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeTrue("Primeval Titan's printed rider taps the land after the move");
    }

    // -----------------------------------------------------------------------
    // Registry-absent fallback — raw zone mutation still works
    // -----------------------------------------------------------------------

    [Fact]
    public void PrimevalTitan_NoRegisteredZoneService_FallsBackToRawZoneMutation()
    {
        // Ensure no service is registered — fall through to raw zone mutation.
        ZoneServiceRegistry.Clear();

        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var titan = PrimevalTitanFactory.Create(
            _alice, triggers: null,
            selector: _ => new ICard[] { forest });
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        // The legacy raw-mutation path still produces the right end state
        // (land on battlefield, tapped) so existing dispatcher/shape tests
        // continue to pass.
        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeTrue();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
