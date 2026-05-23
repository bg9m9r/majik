using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Api.Tests;

public class RemoteAgentTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public async Task Priority_AwaitsCommand_CompletesOnSubmit()
    {
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        task.IsCompleted.Should().BeFalse("nothing submitted yet");

        agent.Submit(new PassPriorityCommand { PlayerId = _alice.Id });
        var action = await task;

        action.Should().Be(PriorityAction.Pass);
    }

    [Fact]
    public async Task PlayLand_Submitted_ResolvesToActionWithCardLookup()
    {
        var land = new Land("Mountain") { Owner = _alice };
        var agent = new RemoteAgent(_alice, cardLookup: id => id == land.InstanceId ? land : null);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        agent.Submit(new PlayLandCommand(land.InstanceId) { PlayerId = _alice.Id });

        var action = await task;
        action.Should().BeOfType<PriorityAction.PlayLand>()
            .Which.Land.Should().BeSameAs(land);
    }

    [Fact]
    public async Task CastSpell_Submitted_ResolvesToActionWithEmptyTargets()
    {
        // Portal hand-click sends CastSpellCommand with empty targets/X/mode.
        // RemoteAgent must resolve that to PriorityAction.CastSpell so the
        // engine's cast dispatcher (TurnDriver -> SpellCastFlow) can then
        // prompt the agent for ChooseTargets / ChooseX / ChooseMode in
        // separate envelopes (CR 601.2b/c/d).
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var agent = new RemoteAgent(_alice, cardLookup: id => id == bolt.InstanceId ? bolt : null);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        agent.Submit(new CastSpellCommand(
            CardInstanceId: bolt.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null) { PlayerId = _alice.Id });

        var action = await task;
        var cast = action.Should().BeOfType<PriorityAction.CastSpell>().Subject;
        cast.Card.Should().BeSameAs(bolt);
        cast.Targets.Should().BeEmpty();
    }

    [Fact]
    public async Task CastSpell_Submitted_WithPrechosenTargets_PreservesThem()
    {
        // Optional path: a client could pre-resolve targets at the cast
        // command. We don't currently rely on this (SpellCastFlow re-prompts
        // anyway), but the resolution must still produce a valid action so
        // future "smart bot" agents that pre-plan targets aren't blocked.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var goblin = new Creature("Goblin", "R", 1, 1) { Owner = _alice };
        var agent = new RemoteAgent(_alice, cardLookup: id =>
            id == bolt.InstanceId ? bolt : id == goblin.InstanceId ? goblin : null);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        agent.Submit(new CastSpellCommand(
            CardInstanceId: bolt.InstanceId,
            TargetInstanceIds: new[] { goblin.InstanceId },
            XValue: null,
            ModeIndex: null) { PlayerId = _alice.Id });

        var action = await task;
        var cast = action.Should().BeOfType<PriorityAction.CastSpell>().Subject;
        cast.Card.Should().BeSameAs(bolt);
        cast.Targets.Should().ContainSingle().Which.Should().BeSameAs(goblin);
    }

    [Fact]
    public async Task Submit_WrongPlayer_Throws()
    {
        var agent = new RemoteAgent(_alice);

        var act = () => agent.Submit(new PassPriorityCommand { PlayerId = Guid.NewGuid() });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*player*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Submit_WhenNothingPending_Throws()
    {
        var agent = new RemoteAgent(_alice);

        var act = () => agent.Submit(new PassPriorityCommand { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no pending*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task MismatchedCommandType_Throws()
    {
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        var act = () => agent.Submit(new MulliganCommand(true) { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*expected*PassPriorityCommand*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DeclareAttackers_PromptRequested_AnnouncesCommandKind()
    {
        // Verifies the wire-up the portal relies on: when the engine
        // requests attackers, the agent fires PromptRequested with the
        // DeclareAttackersCommand type, which becomes "DeclareAttackersCommand"
        // in PromptDto.ExpectedKinds and triggers the attackers overlay.
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        IReadOnlyList<Type>? announced = null;
        agent.PromptRequested += k => announced = k;

        _ = agent.DeclareAttackersAsync(ctx, Array.Empty<Creature>());

        announced.Should().NotBeNull();
        announced!.Should().ContainSingle().Which.Should().Be(typeof(DeclareAttackersCommand));
        agent.HasPending.Should().BeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DeclareBlockers_PromptRequested_AnnouncesCommandKind()
    {
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        IReadOnlyList<Type>? announced = null;
        agent.PromptRequested += k => announced = k;

        _ = agent.DeclareBlockersAsync(ctx, Array.Empty<Creature>(), Array.Empty<Creature>());

        announced.Should().NotBeNull();
        announced!.Should().ContainSingle().Which.Should().Be(typeof(DeclareBlockersCommand));
        agent.HasPending.Should().BeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DeclareAttackers_EmptyCommand_ResolvesToEmptyCombatPlan()
    {
        // CR 508.2 — declaring no attackers is legal. The wire DTO with
        // an empty Attackers list must produce CombatPlan.None so the
        // engine's CombatFlow skips the blockers prompt and returns
        // without further input from the defender.
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        var task = agent.DeclareAttackersAsync(ctx, Array.Empty<Creature>());
        agent.Submit(new DeclareAttackersCommand(Array.Empty<AttackerDeclarationDto>())
        {
            PlayerId = _alice.Id,
        });

        var plan = await task;
        plan.Attackers.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclareAttackers_WithCreatureAndPlayerDefender_BuildsPlan()
    {
        // Portal sends defenderId = opponent.Id (a Player Guid). Resolver
        // hits the player lookup first and returns the Player as the
        // DefendingPlayerOrPlaneswalker.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var bob = new Player("Bob", 20);
        var agent = new RemoteAgent(
            _alice,
            cardLookup: id => id == bear.InstanceId ? bear : null,
            playerLookup: id => id == bob.Id ? bob : id == _alice.Id ? _alice : null);
        var ctx = NewContext();

        var task = agent.DeclareAttackersAsync(ctx, new[] { bear });
        agent.Submit(new DeclareAttackersCommand(new[]
        {
            new AttackerDeclarationDto(bear.InstanceId, bob.Id),
        }) { PlayerId = _alice.Id });

        var plan = await task;
        plan.Attackers.Should().ContainSingle().Which.Attacker.Should().BeSameAs(bear);
        plan.Attackers[0].DefendingPlayerOrPlaneswalker.Should().BeSameAs(bob);
    }

    [Fact]
    public async Task DeclareAttackers_DefenderIsPlaneswalker_FallsBackToCardLookup()
    {
        // CR 508.1c — a creature may attack a planeswalker the defending
        // player controls. The DTO's DefenderId is the planeswalker's
        // InstanceId; player lookup misses, card lookup returns the
        // Planeswalker. Verifies the fallback path.
        var atk = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var pw = new Planeswalker("Chandra", "2RR", 4);
        var agent = new RemoteAgent(
            _alice,
            cardLookup: id => id == atk.InstanceId ? atk : id == pw.InstanceId ? (ICard)pw : null,
            playerLookup: _ => null);
        var ctx = NewContext();

        var task = agent.DeclareAttackersAsync(ctx, new[] { atk });
        agent.Submit(new DeclareAttackersCommand(new[]
        {
            new AttackerDeclarationDto(atk.InstanceId, pw.InstanceId),
        }) { PlayerId = _alice.Id });

        var plan = await task;
        plan.Attackers.Should().ContainSingle()
            .Which.DefendingPlayerOrPlaneswalker.Should().BeSameAs(pw);
    }

    [Fact]
    public async Task DeclareBlockers_EmptyCommand_ResolvesToEmptyBlockPlan()
    {
        // "Block with nothing" is the common case where the defender lets
        // every attacker through. Must produce BlockPlan.None so the
        // damage-step loop proceeds with no assignments.
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        var task = agent.DeclareBlockersAsync(ctx, Array.Empty<Creature>(), Array.Empty<Creature>());
        agent.Submit(new DeclareBlockersCommand(Array.Empty<BlockerDeclarationDto>())
        {
            PlayerId = _alice.Id,
        });

        var plan = await task;
        plan.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclareBlockers_WithAssignment_BuildsPlan()
    {
        // Two creatures: opp's Grizzly Bears attacks; alice's Goblin
        // blocks. Verifies the wire BlockerDeclarationDto resolves both
        // ends to the right Creature references in BlockerDeclaration.
        var attacker = new Creature("Grizzly Bears", "1G", 2, 2);
        var blocker = new Creature("Goblin", "R", 1, 1) { Owner = _alice };
        var agent = new RemoteAgent(
            _alice,
            cardLookup: id => id == attacker.InstanceId ? attacker
                : id == blocker.InstanceId ? (ICard)blocker
                : null);
        var ctx = NewContext();

        var task = agent.DeclareBlockersAsync(ctx, new[] { attacker }, new[] { blocker });
        agent.Submit(new DeclareBlockersCommand(new[]
        {
            new BlockerDeclarationDto(blocker.InstanceId, attacker.InstanceId),
        }) { PlayerId = _alice.Id });

        var plan = await task;
        plan.Blockers.Should().ContainSingle();
        plan.Blockers[0].Blocker.Should().BeSameAs(blocker);
        plan.Blockers[0].Attacker.Should().BeSameAs(attacker);
    }

    // ---------------------------------------------------------------------
    // OrderTriggers — mirrors the wire wire-up used for DeclareAttackers
    // (PR #154) and MulliganCommand (PR #147): RemoteAgent maps the wire
    // DTO's StackObjectIds back to the engine-provided ITriggeredAbility
    // instances and resolves the prompt with the reordered list.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task OrderTriggers_PromptRequested_AnnouncesCommandKind()
    {
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        IReadOnlyList<Type>? announced = null;
        agent.PromptRequested += k => announced = k;

        _ = agent.OrderTriggersAsync(ctx, Array.Empty<ITriggeredAbility>());

        announced.Should().NotBeNull();
        announced!.Should().ContainSingle().Which.Should().Be(typeof(OrderTriggersCommand));
        agent.HasPending.Should().BeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task OrderTriggers_Submitted_ReordersByStackObjectIds()
    {
        // CR 603.3b — when multiple triggered abilities owned by the same
        // player would go on the stack simultaneously, that player chooses
        // the order. The wire command transports only Guids; RemoteAgent
        // must look each id up in the list the engine just handed it.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var a = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        var b = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        var c = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        // Engine offers them in (a, b, c) order; controller asks for (c, a, b).
        var task = agent.OrderTriggersAsync(ctx, new ITriggeredAbility[] { a, b, c });
        agent.Submit(new OrderTriggersCommand(new[] { c.Id, a.Id, b.Id })
        {
            PlayerId = _alice.Id,
        });

        var ordered = await task;
        ordered.Should().HaveCount(3);
        ordered[0].Should().BeSameAs(c);
        ordered[1].Should().BeSameAs(a);
        ordered[2].Should().BeSameAs(b);
    }

    [Fact]
    public async Task OrderTriggers_Submitted_UnknownIdThrows()
    {
        // Defensive: a client supplying a Guid that doesn't match any
        // pending trigger must surface as a clear error rather than
        // silently losing an ability.
        var bear = new Creature("Bear", "G", 1, 1) { Owner = _alice };
        var a = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.OrderTriggersAsync(ctx, new ITriggeredAbility[] { a });

        var act = () => agent.Submit(new OrderTriggersCommand(new[] { Guid.NewGuid() })
        {
            PlayerId = _alice.Id,
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown stack object*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task OrderTriggers_Submitted_PartialListThrows()
    {
        // The controller must order every offered trigger, not a subset.
        // Skipping any ability would lose its effect — the engine relies
        // on receiving the full list back.
        var bear = new Creature("Bear", "G", 1, 1) { Owner = _alice };
        var a = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        var b = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.OrderTriggersAsync(ctx, new ITriggeredAbility[] { a, b });

        var act = () => agent.Submit(new OrderTriggersCommand(new[] { a.Id })
        {
            PlayerId = _alice.Id,
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*expected 2*");
        await Task.CompletedTask;
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice }, _alice, 1, PhaseStateType.Main, new Majik.Core.Stack.Stack());
}
