using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// End-to-end verification of the Miracle reveal-on-draw hook (CR 702.94b):
/// the live <see cref="TurnDriver"/> subscribes to <see cref="CardDrawnEvent"/>
/// and, when the FIRST card a player drew this turn carries a printed miracle
/// cost, opens its one-shot miracle window
/// (<see cref="Majik.Core.Cards.Card.RuntimeMiracleCost"/>) so its controller
/// may cast it from hand for the miracle cost via
/// <see cref="Majik.Core.Costs.MiracleAlternativeCost"/>.
///
/// Proves: (a) the first draw of a miracle card opens the window at the
/// miracle cost, (b) a SECOND draw the same turn does NOT open a window
/// (the "first card you drew this turn" gate), and (c) a non-miracle first
/// draw opens nothing.
/// </summary>
public class MiracleDrawHookTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priority;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MiracleDrawHookTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public void FirstMiracleDraw_OpensMiracleWindowAtMiracleCost()
    {
        _ = NewDriver();
        var terminus = TerminusFactory.Create(_alice);
        InHand(_alice, terminus);

        _bus.Publish(new CardDrawnEvent(terminus, _alice));

        terminus.RuntimeMiracleCost.Should().Be(ManaCost.Parse("{W}"),
            "the first card drawn this turn that has a miracle cost opens its window (CR 702.94b)");
    }

    [Fact]
    public void SecondMiracleDraw_SameTurn_DoesNotOpenWindow()
    {
        _ = NewDriver();
        var firstDraw = TerminusFactory.Create(_alice);
        var secondDraw = TerminusFactory.Create(_alice);
        InHand(_alice, firstDraw);
        InHand(_alice, secondDraw);

        _bus.Publish(new CardDrawnEvent(firstDraw, _alice));   // 1st card this turn
        _bus.Publish(new CardDrawnEvent(secondDraw, _alice));  // 2nd card this turn

        firstDraw.RuntimeMiracleCost.Should().Be(ManaCost.Parse("{W}"));
        secondDraw.RuntimeMiracleCost.Should().BeNull(
            "miracle only triggers on the FIRST card drawn this turn (CR 702.94b)");
    }

    [Fact]
    public void NonMiracleFirstDraw_OpensNoWindow()
    {
        _ = NewDriver();
        var plain = new Majik.Core.Cards.Instant("Lightning Bolt", "{R}");
        plain.SetOwner(_alice);
        plain.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(plain);

        _bus.Publish(new CardDrawnEvent(plain, _alice));

        plain.RuntimeMiracleCost.Should().BeNull();
    }

    private TurnDriver NewDriver() =>
        new TurnDriver(
            players: new[] { _alice, _bob },
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = new ScriptedAgent(),
                [_bob] = new ScriptedAgent(),
            },
            stack: _stack,
            zoneService: _zones,
            triggerManager: _triggers,
            stackResolver: _resolver,
            stateBasedActions: _sba,
            priorityManager: _priority,
            combatFlow: new CombatFlow(_bus, _sba),
            eventBus: _bus);

    private static void InHand(Player owner, Majik.Core.Cards.Card card)
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(card);
    }
}
