using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.35 — Madness fires on REAL effect / cost discards, not just direct
/// <see cref="MadnessHelper"/> calls. These tests drive the production discard
/// chokepoint <see cref="Fx.DiscardCard"/> (the funnel every effect / cost
/// discard routes through) with a bus-wired <see cref="ZoneService"/> in the
/// <see cref="ZoneServiceRegistry"/> — exactly the wiring the live engine
/// installs at game start — and assert a discarded Madness card lands in EXILE
/// (CR 702.35b) and is offered for its madness cost (CR 702.35c), while a
/// non-Madness card still funnels to the graveyard.
/// </summary>
public sealed class MadnessDiscardFunnelTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public MadnessDiscardFunnelTests()
    {
        ZoneServiceRegistry.Clear();
        EventBusRegistry.Clear();
    }

    public void Dispose()
    {
        ZoneServiceRegistry.Clear();
        EventBusRegistry.Clear();
    }

    private (EventBus bus, ZoneService zones) WireGame()
    {
        var bus = new EventBus();
        var replacements = new ReplacementBus();
        var zones = new ZoneService(eventBus: bus, replacements: replacements);
        // Mirror GameDriver.RunGameAsync: register the per-game ZoneService for
        // the player so Fx.DiscardCard's registry lookup finds it.
        ZoneServiceRegistry.Set(_alice, zones);
        EventBusRegistry.Set(_alice, bus);
        return (bus, zones);
    }

    private Card NewInHand(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    [Fact]
    public void EffectDiscard_OfMadnessCard_GoesToExile_NotGraveyard()
    {
        WireGame();
        // Fiery Temper has Madness {R} in the catalog. Build the real card shell.
        var temper = FieryTemperFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(temper);
        temper.SetZone(ZoneType.Hand);

        // An EFFECT discards it (NOT a direct MadnessHelper call) — Fx.Discard is
        // the effect-discard funnel (Mind Rot, Liliana +1, connive, etc.).
        Fx.DiscardCard(_alice, temper, wasCost: false);

        temper.Zone.Should().Be(ZoneType.Exile,
            "a discarded Madness card is exiled instead of going to the graveyard (CR 702.35b)");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(temper);
        _alice.Zones.Exile.GetCards().Should().Contain(temper);
    }

    [Fact]
    public void EffectDiscard_OfMadnessCard_OffersCastForMadnessCost()
    {
        WireGame();
        var temper = FieryTemperFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(temper);
        temper.SetZone(ZoneType.Hand);

        Fx.DiscardCard(_alice, temper, wasCost: false);

        // CR 702.35c — the controller may cast it for its madness cost. The
        // engine models that as a runtime cast-from-exile grant that the
        // ExileCastAlternativeCost reads; the agent proposes the cast on the
        // priority loop (same seam as Ragavan / impulse-draw).
        temper.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the discarding player is offered the madness cast");
        temper.RuntimeExileCastCost.Should().Be(ManaCost.Parse("{R}"),
            "Fiery Temper's madness cost is {R}");

        var altCost = new ExileCastAlternativeCost("madness", ManaCost.Parse("{R}"));
        altCost.CanCastFor(temper, _alice).Should().BeTrue(
            "the exiled Madness card is a legal cast-from-exile source for its controller");
    }

    [Fact]
    public void FxDiscardEffect_OfMadnessCard_RoutesToExile_ViaTheEffectFunnel()
    {
        // Fx.Discard(player, n) is the EFFECT-discard funnel (Mind Rot, Liliana
        // +1, connive's discard leg, …) — NOT a direct MadnessHelper call. It
        // must route the same madness behaviour through DiscardCard.
        WireGame();
        var temper = FieryTemperFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(temper);
        temper.SetZone(ZoneType.Hand);

        var discarded = Fx.Discard(_alice, 1);

        discarded.Should().ContainSingle().Which.Should().BeSameAs(temper);
        temper.Zone.Should().Be(ZoneType.Exile, "an effect discard of a madness card exiles it");
        temper.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EffectDiscard_OfNonMadnessCard_GoesToGraveyard()
    {
        WireGame();
        var plain = NewInHand("Plain Spell");

        Fx.DiscardCard(_alice, plain, wasCost: false);

        plain.Zone.Should().Be(ZoneType.Graveyard,
            "a non-Madness card still funnels to the graveyard");
        plain.RuntimeExileCastAllowedCaster.Should().BeNull("no madness window for a plain card");
    }

    [Fact]
    public void EffectDiscard_StillPublishesDiscardedEvent_ForMadnessCard()
    {
        var (bus, _) = WireGame();
        var captured = new List<DiscardedEvent>();
        bus.Subscribe<DiscardedEvent>(captured.Add);

        var temper = FieryTemperFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(temper);
        temper.SetZone(ZoneType.Hand);

        Fx.DiscardCard(_alice, temper, wasCost: false);

        captured.Should().ContainSingle("discard-matters triggers must still fire on a madness discard");
        captured[0].Card.Should().BeSameAs(temper);
        captured[0].Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CostDiscard_OfMadnessCard_GoesToExile_AndIsACost()
    {
        var (bus, _) = WireGame();
        var captured = new List<DiscardedEvent>();
        bus.Subscribe<DiscardedEvent>(captured.Add);

        var temper = FieryTemperFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(temper);
        temper.SetZone(ZoneType.Hand);

        // A discard COST (e.g. Wild Mongrel "discard a card:") routes the same
        // funnel with wasCost: true.
        Fx.DiscardCard(_alice, temper, wasCost: true);

        temper.Zone.Should().Be(ZoneType.Exile);
        temper.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        captured.Should().ContainSingle();
        captured[0].WasCost.Should().BeTrue("a discard cost is paid as a cost");
    }

    [Fact]
    public void MadnessWindow_ClosesAtEndOfTurn_UncastCardFallsToGraveyard()
    {
        var (bus, _) = WireGame();
        var temper = FieryTemperFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(temper);
        temper.SetZone(ZoneType.Hand);

        Fx.DiscardCard(_alice, temper, wasCost: false);
        temper.Zone.Should().Be(ZoneType.Exile, "discarded into exile, awaiting the madness cast");

        // CR 514.2 — the controller's Cleanup step closes the window. The card
        // was never cast, so CR 702.35c puts it into the graveyard.
        bus.Publish(new StepStartedEvent(
            Majik.Core.StateMachine.StepStateType.Cleanup, _alice));

        temper.Zone.Should().Be(ZoneType.Graveyard,
            "an uncast madness card falls to the graveyard when the window closes (CR 702.35c)");
        temper.RuntimeExileCastAllowedCaster.Should().BeNull("the cast grant is revoked at end of turn");
    }

    [Fact]
    public void Catalog_CoversTheKnownMadnessPool()
    {
        MadnessCatalog.HasMadness(new Card("Fiery Temper", "")).Should().BeTrue();
        MadnessCatalog.HasMadness(new Card("Reckless Wurm", "")).Should().BeTrue();
        MadnessCatalog.HasMadness(new Card("Big Game Hunter", "")).Should().BeTrue();
        MadnessCatalog.HasMadness(new Card("Grizzly Bears", "")).Should().BeFalse();
        MadnessCatalog.CostFor(new Card("Call to the Netherworld", ""))
            .Should().Be(ManaCost.Parse("{0}"));
        MadnessCatalog.Count.Should().BeGreaterThan(40, "the Modern pool has 40+ Madness cards");
    }
}
