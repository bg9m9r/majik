using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Snapback (Time Spiral, {2}{U}).
/// Mirrors the Force-of-Negation / Force-of-Despair test shape:
///   * Card shape + dispatch.
///   * Pitch cast — exiles a blue card, no timing gate (any turn).
///   * Resolve bounces target creature to its owner's hand.
///   * Illegal target (creature no longer on battlefield) → no-op.
/// </summary>
public class SnapbackFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SnapbackFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var snap = SnapbackFactory.Create(_alice);

        snap.Name.Should().Be("Snapback");
        snap.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(snap).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsSnapbackShape()
    {
        var dispatched = NamedCardFactory.Create("Snapback", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Snapback");
    }

    [Fact]
    public async Task CastViaPitch_ExilesBlueCard_BouncesTargetCreature()
    {
        var snap = SnapbackFactory.Create(_alice);
        snap.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snap);

        var pitchFuel = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        pitchFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchFuel);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var pitchCost = new ExileColoredCardAlternativeCost(ManaColor.Blue, pitchFuel);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobBear });
        agent.QueueMana(ManaPayment.Empty);
        // On Alice's own turn — Snapback's pitch has NO timing restriction.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, snap,
            SnapbackFactory.BuildDefinition(o => o, _zones),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        pitchFuel.Zone.Should().Be(ZoneType.Exile,
            because: "pitched blue card is exiled (CR 117.11 + CR 701.21)");
        bobBear.Zone.Should().Be(ZoneType.Hand,
            because: "Snapback returns target creature to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bobBear);
    }

    [Fact]
    public async Task CastWithoutPitch_PrintedCost_BouncesTargetCreature()
    {
        // Printed-mana cast — no alternative cost. Snapback is castable at
        // instant speed any time (CR 117.1).
        var snap = SnapbackFactory.Create(_alice);
        snap.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snap);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobBear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, snap,
            SnapbackFactory.BuildDefinition(o => o, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Hand,
            because: "Snapback returns target creature to owner's hand on resolve");
    }

    [Fact]
    public void Resolve_IllegalTarget_IsNoOp()
    {
        // CR 608.2b — target moved off the battlefield before resolution:
        // the bounce no-ops cleanly.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        // NOT on the battlefield — already in graveyard pre-resolution.
        bobBear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobBear);

        var def = SnapbackFactory.BuildDefinition(o => o, _zones);
        var picks = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new object[] { bobBear } },
            ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "illegal target (not on battlefield) → no bounce");
    }
}
