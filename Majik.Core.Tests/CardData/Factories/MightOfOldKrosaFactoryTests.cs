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
/// Tests for Might of Old Krosa (Time Spiral, {G}, Instant).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Target creature gets +2/+2 until end of turn. If you cast this spell
///    during your main phase, that creature gets +4/+4 until end of turn
///    instead."
///
/// Coverage:
/// - Card identity (Instant, green, {G}, owner/controller wired).
/// - NamedCardFactory dispatcher returns the correct shape.
/// - SpellDefinition shape (1 target creature, no modes, no X).
/// - Cast during a main phase → +4/+4 (CR 514.2 — the conditional "instead").
/// - Cast outside a main phase → +2/+2 (the printed base value).
/// - The chosen amount expires at end of turn (CR 514.2).
/// - Fizzle: target not on battlefield → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "G")]
public class MightOfOldKrosaFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MightOfOldKrosaFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Green_AtCostG()
    {
        var mok = MightOfOldKrosaFactory.Create(_alice);

        mok.Name.Should().Be("Might of Old Krosa");
        mok.ManaCost.Should().Be("{G}");
        mok.HasType(CardType.Instant).Should().BeTrue();
        mok.Owner.Should().BeSameAs(_alice);
        mok.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(mok).Should().Contain(ManaColor.Green);
    }
    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = MightOfOldKrosaFactory.BuildDefinition(castDuringMainPhase: true);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task CastDuringMainPhase_TargetGetsPlusFourPlusFour()
    {
        var bear = BuildBear(_alice);

        var mok = MightOfOldKrosaFactory.Create(_alice);
        mok.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mok);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);

        // Alice's own precombat main phase (CR 505) — the "instead" clause applies.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, mok, MightOfOldKrosaFactory.BuildDefinition(ctx, _alice), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(6);
        bear.GetToughness().Should().Be(6);
    }

    [Fact]
    public async Task CastOutsideMainPhase_TargetGetsPlusTwoPlusTwo()
    {
        var bear = BuildBear(_alice);

        var mok = MightOfOldKrosaFactory.Create(_alice);
        mok.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mok);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);

        // Cast at instant speed during Alice's upkeep — NOT a main phase, so the
        // base +2/+2 applies (CR 116.3a — instants may be cast any time priority
        // is held; the conditional only triggers in a main phase).
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Upkeep, _stack);

        await _flow.CastAsync(_alice, mok, MightOfOldKrosaFactory.BuildDefinition(ctx, _alice), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(4);
    }

    [Fact]
    public async Task CastDuringOpponentsMainPhase_TargetGetsPlusTwoPlusTwo()
    {
        var bear = BuildBear(_alice);

        var mok = MightOfOldKrosaFactory.Create(_alice);
        mok.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mok);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);

        // It is Bob's main phase, not Alice's. "Your main phase" means the
        // caster's own main phase (only the active player has a main phase,
        // CR 505), so casting on Bob's turn yields the base +2/+2.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, mok, MightOfOldKrosaFactory.BuildDefinition(ctx, _alice), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(4);
    }

    [Fact]
    public async Task PumpEffect_ExpiresAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        var mok = MightOfOldKrosaFactory.Create(_alice);
        mok.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mok);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, mok, MightOfOldKrosaFactory.BuildDefinition(ctx, _alice), agent, ctx);
        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(6);

        // CR 514.2 — until end of turn effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // Bob's creature is in the graveyard at resolution (CR 608.2b).
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(dead);

        var mok = MightOfOldKrosaFactory.Create(_alice);
        mok.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mok);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { dead });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, mok, MightOfOldKrosaFactory.BuildDefinition(ctx, _alice), agent, ctx);
        _resolver.ResolveTop(_stack);

        dead.GetPower().Should().Be(2);
        dead.GetToughness().Should().Be(2);
    }

    private Creature BuildBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        bear.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }
}
