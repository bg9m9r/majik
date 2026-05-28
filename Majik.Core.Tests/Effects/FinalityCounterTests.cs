using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 122.1m — Finality counter die-replacement tests. Covers:
/// <list type="bullet">
///   <item><see cref="CounterType.Finality"/> is a registered counter type.</item>
///   <item>A creature with a finality counter dying → exile (not graveyard)
///       for the SBA death path (lethal damage), the destroy path, the
///       sacrifice path.</item>
///   <item>Creatures WITHOUT a finality counter are not redirected.</item>
///   <item>Multiple finality counters behave identically to one.</item>
///   <item>Other counters on the creature (charge, +1/+1, …) survive the
///       move (well — the counter bag is the post-zone-change bag; the
///       relevant invariant is that non-finality counters don't affect
///       the redirect gate).</item>
///   <item><see cref="FinalityCounterReplacement.Register"/> is idempotent
///       on the same bus.</item>
/// </list>
/// </summary>
public class FinalityCounterTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FinalityCounterType_Exists_AndIsNamedFinality()
    {
        CounterType.Finality.Should().NotBeNull();
        CounterType.Finality.Name.Should().Be("Finality");
    }

    [Fact]
    public void CreatureWithFinalityCounter_OnLethalDamage_GoesToExile_NotGraveyard()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);
        var sba = new StateBasedActions(eventBus, zones);
        FinalityCounterReplacement.Register(rep);

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Counters.Add(CounterType.Finality, 1);

        bear.TakeDamage(5);
        sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { bear });

        bear.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void CreatureWithoutFinalityCounter_OnLethalDamage_GoesToGraveyard()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);
        var sba = new StateBasedActions(eventBus, zones);
        FinalityCounterReplacement.Register(rep);

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        bear.TakeDamage(5);
        sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { bear });

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void CreatureWithFinalityCounter_OnDirectZoneMove_GoesToExile()
    {
        // Sacrifice / destroy / SBA all funnel through ZoneService.MoveCard
        // with a Battlefield → Graveyard intent. Direct call here mirrors
        // the sacrifice path (CR 701.16).
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus: null, replacements: rep);
        FinalityCounterReplacement.Register(rep);

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Counters.Add(CounterType.Finality, 1);

        zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        bear.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void MultipleFinalityCounters_StillRedirectOnce_ToExile()
    {
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus: null, replacements: rep);
        FinalityCounterReplacement.Register(rep);

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Counters.Add(CounterType.Finality, 3);

        zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        bear.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void OtherCounters_AlongsideFinality_StillRedirect()
    {
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus: null, replacements: rep);
        FinalityCounterReplacement.Register(rep);

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Counters.Add(CounterType.PlusOnePlusOne, 2);
        bear.Counters.Add(CounterType.Finality, 1);

        zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        bear.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void NonBattlefieldToGraveyardMove_NotRedirected_EvenWithFinalityCounter()
    {
        // E.g. graveyard → exile (theoretical), library → graveyard (mill).
        // The replacement only fires on Battlefield → Graveyard.
        var rep = new ReplacementBus();
        FinalityCounterReplacement.Register(rep);

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.Counters.Add(CounterType.Finality, 1);

        var intent = new ZoneMoveIntent(
            bear, ZoneType.Library, ZoneType.Graveyard, _alice);

        FinalityCounterReplacement.Applies(intent).Should().BeFalse();
    }

    [Fact]
    public void Register_IsIdempotent_OnSameBus()
    {
        var rep = new ReplacementBus();
        var first = FinalityCounterReplacement.Register(rep);
        var second = FinalityCounterReplacement.Register(rep);

        second.Should().BeSameAs(first);
    }
}
