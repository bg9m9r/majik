using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// Verifies that <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/>
/// prompts the controller's agent for targets before pushing triggers that
/// declare <see cref="TriggeredAbility.TargetRequests"/> (Rule 603.3).
/// </summary>
public class TriggerManagerTargetingTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _manager;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TriggerManagerTargetingTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _manager = new TriggerManager(_stack, _bus);
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_PromptsTargets_BeforePush()
    {
        // Triggered ability that requires one target.
        var req = new TargetRequest("target player", 1, 1, new object[] { _bob });
        var ability = BuildEtbAbilityWithTargetRequests(_alice, targetRequests: new[] { req });

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        _manager.PendingCount.Should().Be(1);

        // Agent picks Bob as the target.
        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueTargets(new object[] { _bob });

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };
        var ctx = NewContext();

        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, ctx);

        // Ability is now on the stack with Bob as the chosen target.
        _stack.Count.Should().Be(1);
        ability.ChosenTargets.Should().HaveCount(1);
        ability.ChosenTargets[0].Should().ContainSingle().Which.Should().BeSameAs(_bob);
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_MultipleRequests_AllCollected()
    {
        var req1 = new TargetRequest("target player 1", 1, 1, new object[] { _bob });
        var req2 = new TargetRequest("target player 2", 1, 1, new object[] { _alice });
        var ability = BuildEtbAbilityWithTargetRequests(_alice,
            targetRequests: new[] { req1, req2 });

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueTargets(new object[] { _bob });
        agent.QueueTargets(new object[] { _alice });

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };

        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        ability.ChosenTargets.Should().HaveCount(2);
        ability.ChosenTargets[0].Should().ContainSingle().Which.Should().BeSameAs(_bob);
        ability.ChosenTargets[1].Should().ContainSingle().Which.Should().BeSameAs(_alice);
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_NoTargetRequests_SkipsPrompt()
    {
        // Standard trigger with no target requests — agent is NOT asked for targets.
        var source = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        // Agent has no targets queued; would throw if ChooseTargetsAsync were called.
        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };

        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        _stack.Count.Should().Be(1);
        ability.ChosenTargets.Should().BeEmpty();
    }

    [Fact]
    public void TriggeredAbility_TargetRequests_DefaultsToEmpty()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        ability.TargetRequests.Should().BeEmpty();
        ability.ChosenTargets.Should().BeEmpty();
    }

    [Fact]
    public void TriggeredAbility_SetChosenTargets_NullArg_SetsEmpty()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        ability.SetChosenTargets(null!);

        ability.ChosenTargets.Should().BeEmpty();
    }

    [Fact]
    public void TriggeredAbility_TargetRequests_StoredFromConstructor()
    {
        var req = new TargetRequest("target creature", 1, 1, System.Array.Empty<object>());
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source),
            targetRequests: new[] { req });

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].Should().BeSameAs(req);
    }

    // -----------------------------------------------------------------------
    // CR 700.2d / CR 603.3 — agent-driven mode prompt at stack entry.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_PromptsMode_RecordsChoiceOnAbility()
    {
        // A modal ETB trigger ("choose one —" with three modes).
        var modeReq = new ModeRequest(
            Modes: new[] { "Mode A", "Mode B", "Mode C" },
            MinModes: 1, MaxModes: 1);
        var ability = BuildEtbAbilityWithModeRequest(_alice, modeReq);

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        // Agent chooses mode index 2 at stack entry.
        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueMode(2);

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };

        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        _stack.Count.Should().Be(1);
        ability.ChosenModes.Should().NotBeNull();
        ability.ChosenModes!.Should().ContainSingle().Which.Should().Be(2,
            "the engine prompts the agent for the mode at stack entry (CR 700.2d) "
            + "and records it on the ability, mirroring ChosenTargets");
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_NoModeRequest_LeavesChosenModesNull()
    {
        var source = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        // Agent has no mode queued; would throw if ChooseModeAsync were called.
        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };

        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        _stack.Count.Should().Be(1);
        ability.ChosenModes.Should().BeNull("a non-modal trigger declares no ModeRequest");
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_NoAgent_LeavesChosenModesNull()
    {
        // CR 700.2d — no agent registered for the controller: nothing is
        // recorded, so the effect body falls back to its factory default.
        var modeReq = new ModeRequest(new[] { "Mode A", "Mode B" }, 1, 1);
        var ability = BuildEtbAbilityWithModeRequest(_alice, modeReq);

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        // Empty agents map.
        await _manager.PutPendingTriggersOnStackAsync(
            _alice, new Dictionary<Player, IPlayerAgent>(), NewContext());

        _stack.Count.Should().Be(1);
        ability.ChosenModes.Should().BeNull();
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_ChosenMode_ThreadedIntoResolutionContext()
    {
        // End-to-end: the engine-recorded mode is threaded into
        // ResolutionContext.ChosenMode at resolve time.
        var modeReq = new ModeRequest(new[] { "Mode A", "Mode B", "Mode C" }, 1, 1);

        int? observedMode = null;
        var source = new Creature($"Modal-{Guid.NewGuid()}", "1G", 2, 2)
            { Owner = _alice, Zone = ZoneType.Battlefield };
        var effect = new Effect("observe chosen mode", ctx =>
        {
            observedMode = ctx.ChosenMode;
            return ValueTask.CompletedTask;
        });
        var ability = new TriggeredAbility(
            source, _alice,
            Triggers.OnEnterBattlefieldSelf(source),
            effects: new[] { effect },
            modeRequest: modeReq);

        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueMode(1);

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };
        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        await ability.ResolveAsync(agent, NewContext());

        observedMode.Should().Be(1,
            "the chosen mode is threaded into ResolutionContext.ChosenMode (CR 700.2d)");
    }

    [Fact]
    public void TriggeredAbility_ModeRequest_DefaultsToNull()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        ability.ModeRequest.Should().BeNull();
        ability.ChosenModes.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // CR 601.2d / CR 119.4 — agent-driven divide-damage prompt at stack entry
    // for a "deals N damage divided as you choose among …" TRIGGERED ability
    // (Inferno Titan's enters-or-attacks trigger, Fury's ETB). The engine
    // prompts AFTER target collection (Rule 603.3) and records the split on the
    // ability, threading it into ResolutionContext.DamageDivision at resolve.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_PromptsDamageDivision_RecordsOnAbility()
    {
        var req = new TargetRequest("any target", 1, 3,
            new object[] { _alice, _bob });
        var ability = BuildEtbAbilityWithDivision(
            _alice, new[] { req }, new DamageDivisionSpec(3, TargetSlotIndex: 0));

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueTargets(new object[] { _alice, _bob });
        agent.QueueDamageDivision(1, 2); // Alice 1, Bob 2 — NOT the 2/1 even split

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };
        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        _stack.Count.Should().Be(1);
        ability.ChosenDamageDivision.Should().NotBeNull();
        ability.ChosenDamageDivision!.Should().HaveCount(2);
        ability.ChosenDamageDivision[0].Should().BeEquivalentTo(
            new { Target = _alice, TargetSlotPosition = 0, Amount = 1 });
        ability.ChosenDamageDivision[1].Should().BeEquivalentTo(
            new { Target = _bob, TargetSlotPosition = 1, Amount = 2 });
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_NoDamageDivisionSpec_LeavesNull()
    {
        var req = new TargetRequest("target player", 1, 1, new object[] { _bob });
        var ability = BuildEtbAbilityWithTargetRequests(_alice, new[] { req });

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueTargets(new object[] { _bob });
        // No QueueDamageDivision — ChooseDamageDivisionAsync must NOT be called.

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };
        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        ability.ChosenDamageDivision.Should().BeNull(
            "a trigger that declares no DamageDivisionSpec is never prompted");
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_NoAgent_LeavesDamageDivisionNull()
    {
        // No agent registered: nothing is recorded, the effect body falls back
        // to an even split — behaviour-preserving for the no-agent dispatcher.
        var req = new TargetRequest("any target", 1, 3, new object[] { _alice, _bob });
        var ability = BuildEtbAbilityWithDivision(
            _alice, new[] { req }, new DamageDivisionSpec(3, TargetSlotIndex: 0));

        ((ICard)ability.Source).SetZone(ZoneType.Battlefield);
        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent((ICard)ability.Source, ZoneType.Hand, ZoneType.Battlefield));

        await _manager.PutPendingTriggersOnStackAsync(
            _alice, new Dictionary<Player, IPlayerAgent>(), NewContext());

        ability.ChosenDamageDivision.Should().BeNull();
    }

    [Fact]
    public async Task PutPendingTriggersOnStackAsync_AnnouncedSplit_ThreadedAndDealtVerbatim()
    {
        // End-to-end: the recorded split is threaded into
        // ResolutionContext.DamageDivision and Fx.DealDividedDamageAny deals
        // the announced amounts (Alice 1, Bob 2 — a skew the even split for two
        // targets would NOT produce).
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        _bob.Zones.Battlefield.AddCard(grizzly);

        var req = new TargetRequest("any target", 1, 3,
            new object[] { _bob, grizzly });

        var source = new Creature($"Titan-{Guid.NewGuid()}", "4RR", 6, 6)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var effect = new Effect("deal 3 divided",
            rc => { Majik.Core.Primitives.Fx.DealDividedDamageAny(rc, 3, source); return ValueTask.CompletedTask; });
        var ability = new TriggeredAbility(
            source, _alice,
            Triggers.OnEnterBattlefieldSelf(source),
            effects: new[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { req },
            damageDivision: new DamageDivisionSpec(3, TargetSlotIndex: 0));

        _manager.RegisterTriggeredAbility(ability);
        _manager.EvaluateTriggers(
            new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));

        var agent = new ScriptedAgent();
        agent.QueueTriggerOrder(new ITriggeredAbility[] { ability });
        agent.QueueTargets(new object[] { _bob, grizzly });
        agent.QueueDamageDivision(1, 2); // Bob 1, Grizzly 2

        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };
        await _manager.PutPendingTriggersOnStackAsync(_alice, agents, NewContext());

        var bobLife = _bob.LifeTotal;
        await ability.ResolveAsync(agent, NewContext());

        _bob.LifeTotal.Should().Be(bobLife - 1, "Bob took the announced 1 damage");
        grizzly.Damage.Should().Be(2, "Grizzly took the announced 2 damage");
    }

    [Fact]
    public void TriggeredAbility_DamageDivision_DefaultsToNull()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var ability = new TriggeredAbility(source, _alice,
            Triggers.OnEnterBattlefieldSelf(source));

        ability.DamageDivision.Should().BeNull();
        ability.ChosenDamageDivision.Should().BeNull();
    }

    private TriggeredAbility BuildEtbAbilityWithDivision(
        Player controller,
        IEnumerable<TargetRequest> targetRequests,
        DamageDivisionSpec spec)
    {
        var source = new Creature($"Titan-{Guid.NewGuid()}", "4RR", 6, 6)
            { Owner = controller };
        return new TriggeredAbility(
            source, controller,
            Triggers.OnEnterBattlefieldSelf(source),
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: targetRequests,
            damageDivision: spec);
    }

    // ---- helpers -----------------------------------------------------------

    private TriggeredAbility BuildEtbAbilityWithModeRequest(
        Player controller, ModeRequest modeRequest)
    {
        var source = new Creature($"Modal-{Guid.NewGuid()}", "1G", 2, 2)
            { Owner = controller };
        return new TriggeredAbility(
            source, controller,
            Triggers.OnEnterBattlefieldSelf(source),
            modeRequest: modeRequest);
    }

    private TriggeredAbility BuildEtbAbilityWithTargetRequests(
        Player controller,
        IEnumerable<TargetRequest> targetRequests)
    {
        var source = new Creature($"Bear-{Guid.NewGuid()}", "1G", 2, 2)
            { Owner = controller };
        return new TriggeredAbility(
            source, controller,
            Triggers.OnEnterBattlefieldSelf(source),
            targetRequests: targetRequests);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
}
