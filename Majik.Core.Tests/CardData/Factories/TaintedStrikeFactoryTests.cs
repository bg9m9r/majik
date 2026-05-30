using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tainted Strike (New Phyrexia, {B}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target creature gets +1/+0 and gains infect until end of turn. (It
///    deals damage to creatures in the form of -1/-1 counters and to players
///    in the form of poison counters.)"
///
/// Coverage:
/// - Card identity (Instant, black, {B}, owner/controller wired) loaded from
///   the embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatcher returns the correct shape.
/// - Spell-definition shape: single 1..1 "target creature" request, no X.
/// - Cast + resolve: target gets +1/+0 (CR 613.1g) and gains Infect EOT
///   (CR 702.90).
/// - EOT cleanup expires both the pump and the keyword grant (CR 514.2).
/// - Fizzle: target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
public class TaintedStrikeFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TaintedStrikeFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Black_AtCostB()
    {
        var ts = TaintedStrikeFactory.Create(_alice);

        ts.Name.Should().Be("Tainted Strike");
        ts.ManaCost.Should().Be("{B}");
        ts.HasType(CardType.Instant).Should().BeTrue();
        ts.Owner.Should().BeSameAs(_alice);
        ts.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(ts).Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsTaintedStrikeShape()
    {
        var dispatched = NamedCardFactory.Create("Tainted Strike", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Tainted Strike");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = TaintedStrikeFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cast_TargetGetsPlusOnePlusZeroAndGainsInfect()
    {
        var bear = BuildBear(_alice);

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(3, because: "Tainted Strike is +1/+0 on top of a 2/2");
        bear.GetToughness().Should().Be(2, because: "Tainted Strike grants no toughness");
        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Infect").Should().BeTrue(
                because: "Tainted Strike grants infect until end of turn (CR 702.90)");
    }

    [Fact]
    public async Task EffectsExpireAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(3);
        svc.Compute(bear).Keywords.Contains("Infect").Should().BeTrue();

        // Simulate end-of-turn cleanup (CR 514.2 — EOT effects expire).
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2, because: "the +1/+0 expires at cleanup");
        bear.GetToughness().Should().Be(2);
        svc.Compute(bear).Keywords.Contains("Infect").Should().BeFalse(
            because: "GrantKeywordUntilEndOfTurnEffect expires at cleanup (CR 514.2)");
    }

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // CR 608.2b — illegal target at resolution → no-op.
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(dead);

        await CastAndResolve(dead);

        dead.GetPower().Should().Be(2);
        dead.GetToughness().Should().Be(2);
        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Infect").Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature BuildBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        bear.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }

    private async Task CastAndResolve(object target)
    {
        var ts = TaintedStrikeFactory.Create(_alice);
        ts.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ts);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, ts,
            TaintedStrikeFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}
