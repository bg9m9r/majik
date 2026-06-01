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
        _alice.Zones.Hand.AddCard(land);
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
        _alice.Zones.Hand.AddCard(bolt);
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
        _alice.Zones.Hand.AddCard(bolt);
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

    // ── ExpectedCommandKinds narrowing (PR: ChoosePriorityActionAsync legality gate) ──
    //
    // Portal auto-passes "true pass-only" priority windows by checking
    // ExpectedCommandKinds === ['PassPriorityCommand']. The old behaviour
    // (always advertise Pass + PlayLand + CastSpell) disabled that gate.
    // These tests pin down the legality narrowing so a regression there is
    // a build break, not a silent UX bug.

    [Fact]
    public async Task PriorityKinds_EmptyHand_OnlyAdvertisesPass()
    {
        // Untap/upkeep/draw/cleanup with nothing in hand → there is
        // literally nothing the player could do but pass. Portal relies on
        // the singleton kinds list to auto-pass without prompting.
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().BeEquivalentTo(new[] { typeof(PassPriorityCommand) });
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PriorityKinds_LandInHand_SorceryWindow_AdvertisesPlayLand()
    {
        // Active player's main phase + empty stack + land in hand → land
        // drop is legal (CR 305.2). LandDropTracker per-turn cap is not
        // checked here (engine validates on submit); we just need to keep
        // the option visible to the user.
        var land = new Land("Mountain") { Owner = _alice };
        _alice.Zones.Hand.AddCard(land);
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().Contain(typeof(PlayLandCommand));
        agent.ExpectedCommandKinds.Should().Contain(typeof(PassPriorityCommand));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PriorityKinds_LandInHand_NotMainPhase_OmitsPlayLand()
    {
        // CR 305.2 — lands are sorcery-speed-only. A priority window in
        // upkeep / draw / combat / end with the active player still
        // shouldn't advertise PlayLand even with a land in hand.
        var land = new Land("Mountain") { Owner = _alice };
        _alice.Zones.Hand.AddCard(land);
        var agent = new RemoteAgent(_alice);
        var ctx = new GameContext(
            _alice, new[] { _alice }, _alice, 1,
            PhaseStateType.Upkeep, new Majik.Core.Stack.Stack());

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().NotContain(typeof(PlayLandCommand));
    }

    [Fact]
    public async Task PriorityKinds_LandInHand_OpponentTurn_OmitsPlayLand()
    {
        // CR 305.2 — lands only on your own turn. Even Main phase + empty
        // stack on the opponent's turn must hide PlayLand.
        var bob = new Player("Bob", 20);
        var land = new Land("Mountain") { Owner = _alice };
        _alice.Zones.Hand.AddCard(land);
        var agent = new RemoteAgent(_alice);
        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, bob },
            activePlayer: bob,
            turnNumber: 1,
            currentPhase: PhaseStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack());

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().NotContain(typeof(PlayLandCommand));
    }

    [Fact]
    public async Task PriorityKinds_InstantInHand_OpponentTurn_AdvertisesCastSpell()
    {
        // CR 307.1 / 117.1 — instants are castable any time a player has
        // priority. On the opponent's untap-step priority window (no, wait:
        // untap has no priority; use End step) an instant should still be
        // offered.
        var bob = new Player("Bob", 20);
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Hand.AddCard(bolt);
        var agent = new RemoteAgent(_alice);
        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, bob },
            activePlayer: bob,
            turnNumber: 1,
            currentPhase: PhaseStateType.End,
            stack: new Majik.Core.Stack.Stack());

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().Contain(typeof(CastSpellCommand));
    }

    [Fact]
    public async Task PriorityKinds_SorceryInHand_OpponentTurn_OmitsCastSpell()
    {
        // CR 307.1 — sorceries need sorcery speed. On the opponent's end
        // step with only a sorcery in hand, CastSpell should be omitted so
        // the portal auto-passes the dead window.
        var bob = new Player("Bob", 20);
        var sorcery = new Sorcery("Wrath of God", "2WW") { Owner = _alice };
        _alice.Zones.Hand.AddCard(sorcery);
        var agent = new RemoteAgent(_alice);
        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, bob },
            activePlayer: bob,
            turnNumber: 1,
            currentPhase: PhaseStateType.End,
            stack: new Majik.Core.Stack.Stack());

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().BeEquivalentTo(new[] { typeof(PassPriorityCommand) });
    }

    [Fact]
    public async Task PriorityKinds_SorceryInHand_SorceryWindow_AdvertisesCastSpell()
    {
        // Own main phase + empty stack with a sorcery in hand → CastSpell
        // is legal (CR 307.1). Land present too should yield all three.
        var sorcery = new Sorcery("Wrath of God", "2WW") { Owner = _alice };
        _alice.Zones.Hand.AddCard(sorcery);
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().Contain(typeof(CastSpellCommand));
    }

    [Fact]
    public async Task PriorityKinds_FlashCreatureInHand_OpponentTurn_AdvertisesCastSpell()
    {
        // CR 702.8 — Flash lets a creature be cast at instant speed.
        // Conservative narrowing must still surface CastSpell when the
        // only non-land card has Flash, regardless of phase / turn.
        var bob = new Player("Bob", 20);
        var ambusher = new Creature("Vendilion Clique", "1UU", 3, 1) { Owner = _alice };
        ambusher.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flash"));
        _alice.Zones.Hand.AddCard(ambusher);
        var agent = new RemoteAgent(_alice);
        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, bob },
            activePlayer: bob,
            turnNumber: 1,
            currentPhase: PhaseStateType.DeclareAttackers,
            stack: new Majik.Core.Stack.Stack());

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().Contain(typeof(CastSpellCommand));
    }

    [Fact]
    public async Task PriorityKinds_StackNonEmpty_OmitsPlayLand()
    {
        // CR 305.2 — lands require the stack to be empty. With a spell on
        // the stack, even in own main phase, PlayLand must be omitted.
        var land = new Land("Mountain") { Owner = _alice };
        _alice.Zones.Hand.AddCard(land);
        var agent = new RemoteAgent(_alice);
        var stack = new Majik.Core.Stack.Stack();
        // We can't easily push a real stack object here without dragging in
        // a full spell; use a stub that satisfies IStackObject.
        stack.Push(new TestStackObject());
        var ctx = new GameContext(
            _alice, new[] { _alice }, _alice, 1,
            PhaseStateType.PreCombatMain, stack);

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().NotContain(typeof(PlayLandCommand));
    }

    // ── ActivateManaAbility — wire-format → PriorityAction.ActivateManaAbility ──
    //
    // Mana abilities are activated at any priority window (CR 605.1a) and
    // don't pass priority (CR 605.3a). These tests cover the RemoteAgent
    // translation path: colour-disambiguation, empty-string single-ability
    // shortcut, rejection of foreign permanents, and the
    // BuildPriorityKinds advertising the command kind when the player
    // controls a mana source.

    [Fact]
    public async Task ActivateManaAbility_Mountain_ResolvesToManaActionByColor()
    {
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { Majik.Core.Cards.Types.CardSupertype.Basic },
            subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.ChangeController(_alice);
        Majik.Core.CardData.OracleManaBinder.BindBasicLandMana(mountain, _alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == mountain.InstanceId ? (ICard)mountain : null);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        agent.Submit(new ActivateManaAbilityCommand(mountain.InstanceId, "R")
        {
            PlayerId = _alice.Id,
        });

        var action = await task;
        var ma = action.Should().BeOfType<PriorityAction.ActivateManaAbility>().Subject;
        ma.Source.Should().BeSameAs(mountain);
        ma.Ability.ManaGenerated.Red.Should().Be(1);
    }

    [Fact]
    public async Task ActivateManaAbility_EmptyColor_SingleManaAbility_Resolves()
    {
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { Majik.Core.Cards.Types.CardSupertype.Basic },
            subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.ChangeController(_alice);
        Majik.Core.CardData.OracleManaBinder.BindBasicLandMana(mountain, _alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == mountain.InstanceId ? (ICard)mountain : null);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        agent.Submit(new ActivateManaAbilityCommand(mountain.InstanceId, "")
        {
            PlayerId = _alice.Id,
        });

        var action = await task;
        action.Should().BeOfType<PriorityAction.ActivateManaAbility>()
            .Which.Ability.ManaGenerated.Red.Should().Be(1);
    }

    [Fact]
    public async Task ActivateManaAbility_DualLand_PicksBlackVsGreen()
    {
        // Synthetic dual: two ManaAbility instances on one permanent, one
        // adding {B}, one adding {G}. Mirrors the shape OracleManaBinder
        // produces for a shock land like Overgrown Tomb when the dual-
        // modal regex matches.
        var dual = new Land("Overgrown Tomb");
        dual.SetOwner(_alice);
        dual.ChangeController(_alice);
        dual.AddAbility(new ManaAbility(dual, _alice, Majik.Core.ValueObjects.ManaCost.Parse("B")));
        dual.AddAbility(new ManaAbility(dual, _alice, Majik.Core.ValueObjects.ManaCost.Parse("G")));
        _alice.Zones.Battlefield.AddCard(dual);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == dual.InstanceId ? (ICard)dual : null);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        agent.Submit(new ActivateManaAbilityCommand(dual.InstanceId, "G")
        {
            PlayerId = _alice.Id,
        });

        var action = await task;
        action.Should().BeOfType<PriorityAction.ActivateManaAbility>()
            .Which.Ability.ManaGenerated.Green.Should().Be(1);
    }

    [Fact]
    public async Task ActivateManaAbility_EmptyColor_AmbiguousMultipleAbilities_Throws()
    {
        var dual = new Land("Overgrown Tomb");
        dual.SetOwner(_alice);
        dual.ChangeController(_alice);
        dual.AddAbility(new ManaAbility(dual, _alice, Majik.Core.ValueObjects.ManaCost.Parse("B")));
        dual.AddAbility(new ManaAbility(dual, _alice, Majik.Core.ValueObjects.ManaCost.Parse("G")));
        _alice.Zones.Battlefield.AddCard(dual);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == dual.InstanceId ? (ICard)dual : null);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        var act = () => agent.Submit(new ActivateManaAbilityCommand(dual.InstanceId, "")
        {
            PlayerId = _alice.Id,
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*disambiguate*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ActivateManaAbility_OpponentControlled_Throws()
    {
        // Alice needs to control SOMETHING with a mana ability so the
        // ExpectedCommandKinds gate even lets the command through to
        // Resolve; otherwise the wrong-kind check fires first. Stage a
        // Forest under Alice (irrelevant to the assertion) and point the
        // command at Bob's Mountain.
        var bob = new Player("Bob", 20);
        var aliceForest = new Land(
            "Forest",
            supertypes: new[] { Majik.Core.Cards.Types.CardSupertype.Basic },
            subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Forest });
        aliceForest.SetOwner(_alice);
        aliceForest.ChangeController(_alice);
        Majik.Core.CardData.OracleManaBinder.BindBasicLandMana(aliceForest, _alice);
        _alice.Zones.Battlefield.AddCard(aliceForest);

        var mountain = new Land(
            "Mountain",
            supertypes: new[] { Majik.Core.Cards.Types.CardSupertype.Basic },
            subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Mountain });
        mountain.SetOwner(bob);
        mountain.ChangeController(bob);
        Majik.Core.CardData.OracleManaBinder.BindBasicLandMana(mountain, bob);
        bob.Zones.Battlefield.AddCard(mountain);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == mountain.InstanceId ? (ICard)mountain
                : id == aliceForest.InstanceId ? (ICard)aliceForest
                : null);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        var act = () => agent.Submit(new ActivateManaAbilityCommand(mountain.InstanceId, "R")
        {
            PlayerId = _alice.Id,
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not control*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ActivateManaAbility_NoMatchingColor_Throws()
    {
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { Majik.Core.Cards.Types.CardSupertype.Basic },
            subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.ChangeController(_alice);
        Majik.Core.CardData.OracleManaBinder.BindBasicLandMana(mountain, _alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == mountain.InstanceId ? (ICard)mountain : null);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        var act = () => agent.Submit(new ActivateManaAbilityCommand(mountain.InstanceId, "U")
        {
            PlayerId = _alice.Id,
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*producing*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PriorityKinds_HasManaSource_AdvertisesActivateManaAbility()
    {
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { Majik.Core.Cards.Types.CardSupertype.Basic },
            subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.ChangeController(_alice);
        Majik.Core.CardData.OracleManaBinder.BindBasicLandMana(mountain, _alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().Contain(typeof(ActivateManaAbilityCommand));
    }

    [Fact]
    public async Task PriorityKinds_NoManaSource_OmitsActivateManaAbility()
    {
        // Empty battlefield → no mana source → kind must be hidden so the
        // portal doesn't surface a tap-for-mana affordance with nothing
        // to tap.
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().NotContain(typeof(ActivateManaAbilityCommand));
        await Task.CompletedTask;
    }

    // ── ActivateAbility (non-mana) — fetchland-shape regression ──
    //
    // Fetchlands like Polluted Delta print "{T}, Pay 1 life, Sacrifice this:
    // Search your library …". The ability is an IActivatedAbility (NOT
    // IManaAbility — it doesn't add mana to the pool; it uses the stack).
    // Before the fix BuildPriorityKinds only advertised mana abilities, so
    // the remote (human) client never received ActivateAbilityCommand in
    // the prompt's kinds and the portal couldn't surface the fetch action,
    // making the entire fetchland archetype unusable from the UI.

    [Fact]
    public async Task PriorityKinds_HasNonManaActivatedAbility_AdvertisesActivateAbility()
    {
        // Fetchland shape: a permanent with a printed non-mana activated
        // ability. The ability's costs / effects are irrelevant to this
        // gate — BuildPriorityKinds is intentionally permissive, advertising
        // the kind whenever any controlled permanent carries one. Legality
        // (cost-payability, sorcery-speed, etc.) is engine-side on submit.
        var fetch = new Land("Polluted Delta");
        fetch.SetOwner(_alice);
        fetch.ChangeController(_alice);
        fetch.AddAbility(new ActivatedAbility(fetch, _alice));
        _alice.Zones.Battlefield.AddCard(fetch);

        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().Contain(typeof(ActivateAbilityCommand));
    }

    [Fact]
    public async Task PriorityKinds_OnlyManaAbility_OmitsActivateAbility()
    {
        // Sanity: a permanent whose only activated ability is a ManaAbility
        // (basic land) must NOT surface ActivateAbilityCommand — that kind
        // is for the non-mana branch only. ActivateManaAbilityCommand
        // continues to cover the tap-for-mana path.
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { Majik.Core.Cards.Types.CardSupertype.Basic },
            subtypes: new[] { Majik.Core.Cards.Types.CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.ChangeController(_alice);
        Majik.Core.CardData.OracleManaBinder.BindBasicLandMana(mountain, _alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().NotContain(typeof(ActivateAbilityCommand));
        agent.ExpectedCommandKinds.Should().Contain(typeof(ActivateManaAbilityCommand));
    }

    [Fact]
    public async Task PriorityKinds_EmptyBattlefield_OmitsActivateAbility()
    {
        // No permanents → nothing to activate. The kind must stay off the
        // list so the portal's "true pass-only" auto-pass gate isn't broken
        // by a stray affordance with no source.
        var agent = new RemoteAgent(_alice);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        agent.ExpectedCommandKinds.Should().NotContain(typeof(ActivateAbilityCommand));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ActivateAbility_Submitted_ResolvesToPriorityActionWithAbility()
    {
        // Round-trip the wire command through Resolve: the submitted
        // (PermanentInstanceId, AbilityId) pair must map back to the
        // same IActivatedAbility instance the engine sees on the permanent,
        // surfaced as PriorityAction.ActivateAbility.
        var fetch = new Land("Polluted Delta");
        fetch.SetOwner(_alice);
        fetch.ChangeController(_alice);
        var ability = new ActivatedAbility(fetch, _alice);
        fetch.AddAbility(ability);
        _alice.Zones.Battlefield.AddCard(fetch);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == fetch.InstanceId ? (ICard)fetch : null);
        var ctx = NewContext();

        var task = agent.ChoosePriorityActionAsync(ctx);
        agent.Submit(new ActivateAbilityCommand(fetch.InstanceId, ability.Id)
        {
            PlayerId = _alice.Id,
        });

        var action = await task;
        var act = action.Should().BeOfType<PriorityAction.ActivateAbility>().Subject;
        act.Ability.Should().BeSameAs(ability);
    }

    [Fact]
    public async Task ActivateAbility_UnknownAbilityId_Throws()
    {
        // Defence: client must not be able to smuggle an AbilityId that
        // doesn't correspond to an actual non-mana activated ability on
        // the named permanent. Throwing here keeps the engine's invariants
        // intact (no null ability reaches DispatchActivate).
        var fetch = new Land("Polluted Delta");
        fetch.SetOwner(_alice);
        fetch.ChangeController(_alice);
        fetch.AddAbility(new ActivatedAbility(fetch, _alice));
        _alice.Zones.Battlefield.AddCard(fetch);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == fetch.InstanceId ? (ICard)fetch : null);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        var act = () => agent.Submit(new ActivateAbilityCommand(fetch.InstanceId, Guid.NewGuid())
        {
            PlayerId = _alice.Id,
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no non-mana activated ability*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ActivateAbility_OpponentControlled_Throws()
    {
        // Alice needs to control SOMETHING with a non-mana activated
        // ability so the ExpectedCommandKinds gate even lets the command
        // through to Resolve; the wrong-kind check would otherwise fire
        // first and mask the controller assertion under test.
        var bob = new Player("Bob", 20);
        var aliceFetch = new Land("Misty Rainforest");
        aliceFetch.SetOwner(_alice);
        aliceFetch.ChangeController(_alice);
        aliceFetch.AddAbility(new ActivatedAbility(aliceFetch, _alice));
        _alice.Zones.Battlefield.AddCard(aliceFetch);

        var bobFetch = new Land("Polluted Delta");
        bobFetch.SetOwner(bob);
        bobFetch.ChangeController(bob);
        bobFetch.AddAbility(new ActivatedAbility(bobFetch, bob));
        bob.Zones.Battlefield.AddCard(bobFetch);

        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == bobFetch.InstanceId ? (ICard)bobFetch
                : id == aliceFetch.InstanceId ? (ICard)aliceFetch
                : null);
        var ctx = NewContext();

        _ = agent.ChoosePriorityActionAsync(ctx);

        var act = () => agent.Submit(new ActivateAbilityCommand(bobFetch.InstanceId, Guid.NewGuid())
        {
            PlayerId = _alice.Id,
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not control*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseLibraryPick_Submit_ResolvesToPickedCard()
    {
        // CR 701.19a — library search. The base IPlayerAgent default
        // returns candidates[0], which made Green Sun's Zenith silently
        // auto-resolve for remote (human) players. RemoteAgent's override
        // must prompt the client with the candidate list and resolve to
        // the picked card on submission.
        var ferret = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _alice };
        var elf = new Creature("Birds of Paradise", "G", 0, 1) { Owner = _alice };
        var candidates = new ICard[] { ferret, elf };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseLibraryPickAsync(ctx: null, candidates, "green creature card");
        agent.HasPending.Should().BeTrue();
        agent.ExpectedCommandKinds.Should().ContainSingle()
            .Which.Should().Be(typeof(ChooseLibraryPickCommand));

        agent.Submit(new ChooseLibraryPickCommand(elf.InstanceId) { PlayerId = _alice.Id });
        var picked = await task;

        picked.Should().BeSameAs(elf);
    }

    [Fact]
    public async Task ChooseLibraryPick_SubmitNull_ResolvesToNullForFindNothing()
    {
        // CR 701.19a — a player may decline to choose a card from a
        // successful search. Null SelectedInstanceId models that branch
        // (e.g. Green Sun's Zenith finds nothing → spell still shuffles
        // into its owner's library, no creature enters the battlefield).
        var elf = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseLibraryPickAsync(ctx: null, new ICard[] { elf }, "green creature card");
        agent.Submit(new ChooseLibraryPickCommand(SelectedInstanceId: null) { PlayerId = _alice.Id });

        (await task).Should().BeNull();
    }

    [Fact]
    public async Task ChooseLibraryPick_InvalidInstanceId_Throws()
    {
        // Defence: the wire command must not be able to smuggle a pick of
        // a card outside the engine-offered candidate set — that would
        // bypass the search predicate (e.g. tutoring a non-green or
        // out-of-range creature for Green Sun's Zenith).
        var elf = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseLibraryPickAsync(ctx: null, new ICard[] { elf }, "green creature card");
        var act = () => agent.Submit(new ChooseLibraryPickCommand(Guid.NewGuid()) { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not in the offered candidate list*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseLibraryPick_PromptPayload_SnapshotsCandidates()
    {
        // The portal needs the candidate card data to render the picker —
        // the library zone is hidden in GameStateDto (CR 706). RemoteAgent
        // stashes a CardSnapshotDto list in PendingPayload so
        // GameFacade.BuildPrompt can copy it onto the wire PromptDto.
        var elf = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _alice };
        var bop = new Creature("Birds of Paradise", "G", 0, 1) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseLibraryPickAsync(ctx: null, new ICard[] { elf, bop },
            kindLabel: "green creature card with mana value 2 or less");

        agent.PendingPayload.Should().NotBeNull();
        agent.PendingPayload!.Label.Should()
            .Be("green creature card with mana value 2 or less");
        var snapshots = agent.PendingPayload.Candidates;
        snapshots.Should().NotBeNull();
        snapshots!.Should().HaveCount(2);
        snapshots![0].InstanceId.Should().Be(elf.InstanceId);
        snapshots![0].Name.Should().Be("Llanowar Elves");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseLibraryPick_EmptyCandidates_PublishesPromptAndResolvesToNull()
    {
        // Companion to the engine-side LibrarySearch refactor: when a tutor
        // pre-filters down to ZERO candidates, the engine now still prompts
        // the agent so a human searcher SEES the failed search rather than
        // a silent no-op. RemoteAgent must:
        //   1. Publish the prompt (with an empty candidate snapshot list).
        //   2. Still ship the libraryView (the FULL library) so the portal
        //      modal can render every card muted with a single Acknowledge
        //      button.
        //   3. Resolve cleanly to null when the wire command comes back
        //      with no SelectedInstanceId.
        // The library is empty here for simplicity (no cards in the
        // library to even put in libraryView); the key invariant is
        // "prompt publishes; submit-null resolves to null".
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseLibraryPickAsync(
            ctx: null,
            candidates: Array.Empty<ICard>(),
            kindLabel: "green creature card with mana value 5 or less");

        // Prompt published.
        agent.HasPending.Should().BeTrue();
        agent.ExpectedCommandKinds.Should().ContainSingle()
            .Which.Should().Be(typeof(ChooseLibraryPickCommand));

        // Payload carries an empty Candidates list (the portal renders 0
        // eligible cards) but still has the kindLabel + libraryView (the
        // latter mirrors the searcher's library, which is empty in this
        // setup).
        agent.PendingPayload.Should().NotBeNull();
        agent.PendingPayload!.Candidates.Should().NotBeNull();
        agent.PendingPayload!.Candidates!.Should().BeEmpty();
        agent.PendingPayload!.Label.Should()
            .Be("green creature card with mana value 5 or less");
        agent.PendingPayload!.LibraryView.Should().NotBeNull();

        // Acknowledge / decline: portal sends ChooseLibraryPickCommand
        // with SelectedInstanceId = null.
        agent.Submit(new ChooseLibraryPickCommand(SelectedInstanceId: null)
            { PlayerId = _alice.Id });

        (await task).Should().BeNull();
    }

    [Fact]
    public async Task ChooseLibraryPick_PromptPayload_ClearedAfterSubmit()
    {
        // Per-prompt payload must not leak past the prompt that owns it —
        // a subsequent priority prompt should see no library candidates,
        // or the portal would re-render a stale library picker.
        var elf = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseLibraryPickAsync(ctx: null, new ICard[] { elf }, "creature");
        agent.PendingPayload.Should().NotBeNull();

        agent.Submit(new ChooseLibraryPickCommand(elf.InstanceId) { PlayerId = _alice.Id });
        await task;

        agent.PendingPayload.Should().BeNull();
        agent.HasPending.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // CR 701.42 — surveil prompt (PR follow-up: wires what PR #1003 deferred)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ChooseSurveil_Submit_PartitionsPeekedCardsIntoGraveyardAndTopOrder()
    {
        // Engine peeks two cards on surveil 2; client partitions them into
        // "send the first to graveyard, keep the second on top" via the
        // wire ChooseSurveilCommand.
        var top = new Creature("Forest", "", 0, 0) { Owner = _alice };
        var next = new Creature("Mountain", "", 0, 0) { Owner = _alice };
        var peeked = new ICard[] { top, next };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseSurveilDecisionAsync(ctx: null, peeked);
        agent.HasPending.Should().BeTrue();
        agent.ExpectedCommandKinds.Should().ContainSingle()
            .Which.Should().Be(typeof(ChooseSurveilCommand));

        agent.Submit(new ChooseSurveilCommand(
                ToGraveyardInstanceIds: new[] { top.InstanceId },
                TopOrderInstanceIds: new[] { next.InstanceId })
            { PlayerId = _alice.Id });
        var decision = await task;

        decision.ToGraveyard.Should().ContainSingle().Which.Should().BeSameAs(top);
        decision.TopOrder.Should().ContainSingle().Which.Should().BeSameAs(next);
    }

    [Fact]
    public async Task ChooseSurveil_PromptPayload_ExposesPeekedSnapshotsOnSurveilView()
    {
        // Portal needs the peeked card data to render the surveil modal — the
        // library is hidden in GameStateDto (CR 706). RemoteAgent stashes a
        // CardSnapshotDto list in PendingPayload.SurveilView so
        // GameFacade.BuildPrompt can copy it onto the wire PromptDto.
        var top = new Creature("Forest", "", 0, 0) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseSurveilDecisionAsync(ctx: null, new ICard[] { top });

        agent.PendingPayload.Should().NotBeNull();
        agent.PendingPayload!.SurveilView.Should().NotBeNull();
        agent.PendingPayload.SurveilView!.Should().ContainSingle()
            .Which.InstanceId.Should().Be(top.InstanceId);
        agent.PendingPayload.Label.Should().Be("surveil 1");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseSurveil_AllToGraveyard_DefaultDecision()
    {
        // The bot's pre-agent default (and ScriptedAgent's fallback) sends
        // every peeked card to the graveyard. Same partition shape should
        // survive the wire — empty TopOrder + every peeked id in
        // ToGraveyard resolves cleanly.
        var c1 = new Creature("Forest", "", 0, 0) { Owner = _alice };
        var c2 = new Creature("Mountain", "", 0, 0) { Owner = _alice };
        var peeked = new ICard[] { c1, c2 };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseSurveilDecisionAsync(ctx: null, peeked);
        agent.Submit(new ChooseSurveilCommand(
                ToGraveyardInstanceIds: new[] { c1.InstanceId, c2.InstanceId },
                TopOrderInstanceIds: Array.Empty<Guid>())
            { PlayerId = _alice.Id });
        var decision = await task;

        decision.ToGraveyard.Should().BeEquivalentTo(new[] { c1, c2 });
        decision.TopOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task ChooseSurveil_UnknownInstanceId_Throws()
    {
        // Defence: clients can't smuggle an InstanceId the engine didn't peek
        // (would let them rearrange / discard cards beyond the surveil
        // window).
        var top = new Creature("Forest", "", 0, 0) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseSurveilDecisionAsync(ctx: null, new ICard[] { top });
        var act = () => agent.Submit(new ChooseSurveilCommand(
                ToGraveyardInstanceIds: new[] { Guid.NewGuid() },
                TopOrderInstanceIds: Array.Empty<Guid>())
            { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown instance*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseSurveil_PartitionDoesNotCoverPeeked_Throws()
    {
        // Defence: client must partition every peeked card exactly once.
        // Dropping one (or assigning one to both buckets) is a wire-error.
        var c1 = new Creature("Forest", "", 0, 0) { Owner = _alice };
        var c2 = new Creature("Mountain", "", 0, 0) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseSurveilDecisionAsync(ctx: null, new ICard[] { c1, c2 });
        // Only c1 referenced; c2 is dropped on the floor.
        var act = () => agent.Submit(new ChooseSurveilCommand(
                ToGraveyardInstanceIds: new[] { c1.InstanceId },
                TopOrderInstanceIds: Array.Empty<Guid>())
            { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*partitioned 1 cards but engine peeked 2*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseSurveil_DuplicateInstanceId_Throws()
    {
        // Defence: same id in both buckets (or twice in one) — refuse.
        var c1 = new Creature("Forest", "", 0, 0) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseSurveilDecisionAsync(ctx: null, new ICard[] { c1 });
        var act = () => agent.Submit(new ChooseSurveilCommand(
                ToGraveyardInstanceIds: new[] { c1.InstanceId },
                TopOrderInstanceIds: new[] { c1.InstanceId })
            { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*more than once*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseSurveil_PendingPayload_ClearedAfterSubmit()
    {
        var c1 = new Creature("Forest", "", 0, 0) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseSurveilDecisionAsync(ctx: null, new ICard[] { c1 });
        agent.PendingPayload.Should().NotBeNull();
        agent.Submit(new ChooseSurveilCommand(
                ToGraveyardInstanceIds: new[] { c1.InstanceId },
                TopOrderInstanceIds: Array.Empty<Guid>())
            { PlayerId = _alice.Id });
        await task;

        agent.PendingPayload.Should().BeNull();
        agent.HasPending.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // CR 117.x / 605.1 — Yes/No prompt (shock-land "pay 2 life?" choice)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ChooseYesNo_Submit_ResolvesToAnswer_True()
    {
        // CR 117.x — optional "may" prompt. RemoteAgent must stash a
        // YesNoView on the prompt payload and resolve the bool when the
        // client submits a ChooseYesNoCommand.
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseYesNoAsync(
            ctx: null,
            question: "Pay 2 life for Overgrown Tomb to enter untapped?",
            sourceCardName: "Overgrown Tomb");
        agent.HasPending.Should().BeTrue();
        agent.ExpectedCommandKinds.Should().ContainSingle()
            .Which.Should().Be(typeof(ChooseYesNoCommand));

        agent.Submit(new ChooseYesNoCommand(Answer: true) { PlayerId = _alice.Id });
        var answer = await task;

        answer.Should().BeTrue();
    }

    [Fact]
    public async Task ChooseYesNo_Submit_ResolvesToAnswer_False()
    {
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseYesNoAsync(
            ctx: null,
            question: "Pay 2 life for Steam Vents to enter untapped?",
            sourceCardName: "Steam Vents");
        agent.Submit(new ChooseYesNoCommand(Answer: false) { PlayerId = _alice.Id });

        (await task).Should().BeFalse();
    }

    [Fact]
    public async Task ChooseYesNo_PromptPayload_CarriesYesNoView()
    {
        // The portal needs the question + source card label to render the
        // modal title and copy — the engine attaches both onto the
        // PendingPayload so GameFacade.BuildPrompt can forward them.
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseYesNoAsync(
            ctx: null,
            question: "Pay 2 life for Overgrown Tomb to enter untapped?",
            sourceCardName: "Overgrown Tomb");

        agent.PendingPayload.Should().NotBeNull();
        var view = agent.PendingPayload!.YesNoView;
        view.Should().NotBeNull();
        view!.Question.Should().Be("Pay 2 life for Overgrown Tomb to enter untapped?");
        view.SourceCardName.Should().Be("Overgrown Tomb");
        view.YesLabel.Should().Be("Yes", "default label when caller doesn't override");
        view.NoLabel.Should().Be("No", "default label when caller doesn't override");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseYesNo_NullSourceCardName_Allowed()
    {
        // Not every Yes/No prompt has a source card (future may-clauses
        // attached to spells / abilities without a permanent context).
        // The payload must accept null SourceCardName without throwing.
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseYesNoAsync(
            ctx: null,
            question: "Cast it for its alternative cost?",
            sourceCardName: null);

        agent.PendingPayload!.YesNoView!.SourceCardName.Should().BeNull();
        agent.PendingPayload!.YesNoView!.Question.Should().Be(
            "Cast it for its alternative cost?");

        agent.Submit(new ChooseYesNoCommand(Answer: true) { PlayerId = _alice.Id });
        (await task).Should().BeTrue();
    }

    [Fact]
    public async Task ChooseYesNo_PendingPayload_ClearedAfterSubmit()
    {
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseYesNoAsync(ctx: null, question: "ok?", sourceCardName: "X");
        agent.PendingPayload.Should().NotBeNull();

        agent.Submit(new ChooseYesNoCommand(Answer: true) { PlayerId = _alice.Id });
        await task;

        agent.PendingPayload.Should().BeNull();
        agent.HasPending.Should().BeFalse();
    }

    [Fact]
    public async Task ChooseYesNo_EmptyQuestion_Throws()
    {
        var agent = new RemoteAgent(_alice);

        // Argument validation happens synchronously before any Task is
        // returned — Action wrapper observes it without ThrowAsync.
        Action act = () => agent.ChooseYesNoAsync(ctx: null, question: "", sourceCardName: "X");

        act.Should().Throw<ArgumentException>();
        await Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // CR 103.4 — London mulligan "put N on the bottom" prompt
    // (ChooseCardsToBottomCommand). Before the fix RemoteAgent.Resolve had
    // no case for this command, so the wire command fell through to the
    // default throw → MatchService surfaced HTTP 400 invalid-command and the
    // mulligan flow never completed (game stuck). These tests pin the
    // round-trip + the count/in-hand validation + the BottomCount payload.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ChooseCardsToBottom_Submit_ResolvesToChosenCards()
    {
        // After 1 mulligan the player must bottom exactly 1 card. The wire
        // ChooseCardsToBottomCommand carrying that single in-hand instance id
        // must resolve the prompt to the matching ICard (the failing repro of
        // the 400 invalid-command bug).
        var c1 = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var c2 = new Creature("Elf", "G", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(c1);
        _alice.Zones.Hand.AddCard(c2);
        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == c1.InstanceId ? (ICard)c1 : id == c2.InstanceId ? c2 : null);

        var task = agent.ChooseCardsToBottomAsync(
            ctx: null!, hand: new ICard[] { c1, c2 }, countToBottom: 1);
        agent.HasPending.Should().BeTrue();
        agent.ExpectedCommandKinds.Should().ContainSingle()
            .Which.Should().Be(typeof(ChooseCardsToBottomCommand));

        agent.Submit(new ChooseCardsToBottomCommand(new[] { c2.InstanceId })
            { PlayerId = _alice.Id });
        var chosen = await task;

        chosen.Should().ContainSingle().Which.Should().BeSameAs(c2);
    }

    [Fact]
    public async Task ChooseCardsToBottom_TwoMulligans_BottomsExactlyTwo()
    {
        // After 2 mulligans the player must bottom exactly 2 cards; the
        // command listing both in-hand instance ids resolves cleanly.
        var c1 = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var c2 = new Creature("Elf", "G", 1, 1) { Owner = _alice };
        var c3 = new Creature("Wolf", "2G", 3, 3) { Owner = _alice };
        _alice.Zones.Hand.AddCard(c1);
        _alice.Zones.Hand.AddCard(c2);
        _alice.Zones.Hand.AddCard(c3);
        var agent = new RemoteAgent(_alice,
            cardLookup: id => new ICard[] { c1, c2, c3 }.FirstOrDefault(c => c.InstanceId == id));

        var task = agent.ChooseCardsToBottomAsync(
            ctx: null!, hand: new ICard[] { c1, c2, c3 }, countToBottom: 2);
        agent.Submit(new ChooseCardsToBottomCommand(new[] { c1.InstanceId, c3.InstanceId })
            { PlayerId = _alice.Id });
        var chosen = await task;

        chosen.Should().BeEquivalentTo(new[] { c1, c3 });
    }

    [Fact]
    public async Task ChooseCardsToBottom_WrongCount_Throws()
    {
        // Server-side defence: the count must equal the required bottom count
        // for the pending prompt. Sending 2 when 1 is required is rejected,
        // and the prompt stays unsatisfied (game not corrupted).
        var c1 = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var c2 = new Creature("Elf", "G", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(c1);
        _alice.Zones.Hand.AddCard(c2);
        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == c1.InstanceId ? (ICard)c1 : id == c2.InstanceId ? c2 : null);

        var task = agent.ChooseCardsToBottomAsync(
            ctx: null!, hand: new ICard[] { c1, c2 }, countToBottom: 1);
        var act = () => agent.Submit(new ChooseCardsToBottomCommand(
            new[] { c1.InstanceId, c2.InstanceId }) { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*1*");
        task.IsCompleted.Should().BeFalse("the rejected submit must leave the prompt unsatisfied");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseCardsToBottom_ZeroWhenOneRequired_Throws()
    {
        // Sending an empty list when 1 is required is likewise rejected.
        var c1 = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        _alice.Zones.Hand.AddCard(c1);
        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == c1.InstanceId ? (ICard)c1 : null);

        var task = agent.ChooseCardsToBottomAsync(
            ctx: null!, hand: new ICard[] { c1 }, countToBottom: 1);
        var act = () => agent.Submit(new ChooseCardsToBottomCommand(Array.Empty<Guid>())
            { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*1*");
        task.IsCompleted.Should().BeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseCardsToBottom_CardNotInHand_Throws()
    {
        // Server-side defence: every chosen card must currently be in the
        // player's hand. A card resolvable by lookup but living elsewhere
        // (here: never added to hand) is rejected.
        var inHand = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var elsewhere = new Creature("Elf", "G", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(inHand);
        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == inHand.InstanceId ? (ICard)inHand
                : id == elsewhere.InstanceId ? elsewhere : null);

        var task = agent.ChooseCardsToBottomAsync(
            ctx: null!, hand: new ICard[] { inHand }, countToBottom: 1);
        var act = () => agent.Submit(new ChooseCardsToBottomCommand(
            new[] { elsewhere.InstanceId }) { PlayerId = _alice.Id });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*hand*");
        task.IsCompleted.Should().BeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseCardsToBottom_PromptPayload_CarriesBottomCount()
    {
        // The portal renders a "bottom N card(s)" label and gates submission
        // to exactly N. RemoteAgent stashes the required count on the prompt
        // payload so GameFacade.BuildPrompt can forward it onto
        // PromptDto.BottomCount.
        var c1 = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var c2 = new Creature("Elf", "G", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(c1);
        _alice.Zones.Hand.AddCard(c2);
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseCardsToBottomAsync(
            ctx: null!, hand: new ICard[] { c1, c2 }, countToBottom: 2);

        agent.PendingPayload.Should().NotBeNull();
        agent.PendingPayload!.BottomCount.Should().Be(2);
    }

    [Fact]
    public async Task ChooseCardsToBottom_PromptPayload_ClearedAfterSubmit()
    {
        var c1 = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        _alice.Zones.Hand.AddCard(c1);
        var agent = new RemoteAgent(_alice,
            cardLookup: id => id == c1.InstanceId ? (ICard)c1 : null);

        var task = agent.ChooseCardsToBottomAsync(
            ctx: null!, hand: new ICard[] { c1 }, countToBottom: 1);
        agent.PendingPayload.Should().NotBeNull();

        agent.Submit(new ChooseCardsToBottomCommand(new[] { c1.InstanceId })
            { PlayerId = _alice.Id });
        await task;

        agent.PendingPayload.Should().BeNull();
        agent.HasPending.Should().BeFalse();
    }

    private sealed class TestStackObject : Majik.Core.Stack.IStackObject
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Player Controller { get; }
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public bool IsResolving => false;
        public TestStackObject() { Controller = new Player("Stub", 20); }
        public void Resolve() { }
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice }, _alice, 1, PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack());

    // -----------------------------------------------------------------------
    // PLAN 01 Slice D — the unified ChooseAsync prompts (ChooseFromHand /
    // ChooseFromBattlefield / ChooseFromPile / ChooseGiftRecipient) reach the
    // human via the interface-default shim, which delegates to ChooseAsync.
    // RemoteAgent OVERRIDES ChooseAsync to fire a real ChoiceCommand wire
    // prompt, so the human path must AWAIT a command (Brainstorm put-back,
    // edict sacrifice, Wish pile, gift recipient) instead of silently
    // auto-picking candidates[0]. These tests lock that in.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ChooseFromHand_Human_AwaitsRealChoiceCommand_NoAutoPick()
    {
        // Brainstorm-style put-back: two distinct cards in hand. If the human
        // path auto-picked candidates[0] the task would already be complete
        // and the player would never get to choose which to put back.
        var a = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var b = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var agent = new RemoteAgent(
            _alice, cardLookup: id => id == a.InstanceId ? a : id == b.InstanceId ? b : null);

        var task = ((IPlayerAgent)agent).ChooseFromHandAsync(
            _alice, new ICard[] { a, b }, BotIntent.None);

        task.IsCompleted.Should().BeFalse(
            "the human must be prompted — not auto-picked to candidates[0]");
        agent.HasPending.Should().BeTrue();
        agent.ExpectedCommandKinds.Should().Contain(typeof(ChoiceCommand));

        // Player picks the SECOND candidate (proves the pick is honoured, not
        // a silent first-candidate default).
        agent.Submit(new ChoiceCommand(
            ChoiceKind.PickOne.ToString(), new[] { b.InstanceId })
        { PlayerId = _alice.Id });

        var picked = await task;
        picked.Should().BeSameAs(b);
    }

    [Fact]
    public async Task ChooseFromBattlefield_Human_AwaitsRealChoiceCommand_NoAutoPick()
    {
        // Edict / Annihilator sacrifice: the human picks which permanent to
        // sacrifice from a multi-card battlefield.
        var a = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var b = new Creature("Elk", "2G", 3, 3) { Owner = _alice };
        var agent = new RemoteAgent(
            _alice, cardLookup: id => id == a.InstanceId ? a : id == b.InstanceId ? b : null);

        var task = ((IPlayerAgent)agent).ChooseFromBattlefieldAsync(
            _alice, new ICard[] { a, b }, BotIntent.Removal);

        task.IsCompleted.Should().BeFalse("the human must be prompted to sacrifice");
        agent.ExpectedCommandKinds.Should().Contain(typeof(ChoiceCommand));

        agent.Submit(new ChoiceCommand(
            ChoiceKind.PickOne.ToString(), new[] { b.InstanceId })
        { PlayerId = _alice.Id });

        (await task).Should().BeSameAs(b);
    }

    [Fact]
    public async Task ChooseFromPile_Human_AwaitsRealChoiceCommand_NoAutoPick()
    {
        // Wish pile (Karn / Burning Wish / Living Wish): the human picks a
        // card from outside the game rather than auto-grabbing candidates[0].
        var a = new Artifact("Relic", "2") { Owner = _alice };
        var b = new Artifact("Engine", "3") { Owner = _alice };
        var agent = new RemoteAgent(
            _alice, cardLookup: id => id == a.InstanceId ? a : id == b.InstanceId ? b : null);

        var task = ((IPlayerAgent)agent).ChooseFromPileAsync(
            _alice, new ICard[] { a, b }, "your sideboard", BotIntent.Tutor);

        task.IsCompleted.Should().BeFalse("the human must be prompted to wish");
        agent.ExpectedCommandKinds.Should().Contain(typeof(ChoiceCommand));

        agent.Submit(new ChoiceCommand(
            ChoiceKind.PickOne.ToString(), new[] { b.InstanceId })
        { PlayerId = _alice.Id });

        (await task).Should().BeSameAs(b);
    }

    [Fact]
    public async Task ChooseGiftRecipient_Human_AwaitsRealChoiceCommand()
    {
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        var card = new Instant("Bolt", "R") { Owner = _alice };
        var agent = new RemoteAgent(
            _alice,
            playerLookup: id => id == bob.Id ? bob : id == carol.Id ? carol : null);

        var task = ((IPlayerAgent)agent).ChooseGiftRecipientAsync(
            NewContext(), card, "a Food token", new[] { bob, carol });

        task.IsCompleted.Should().BeFalse("the human must be prompted to pick a recipient");
        agent.ExpectedCommandKinds.Should().Contain(typeof(ChoiceCommand));

        agent.Submit(new ChoiceCommand(
            ChoiceKind.PickOne.ToString(), new[] { carol.Id })
        { PlayerId = _alice.Id });

        (await task).Should().BeSameAs(carol);
    }

    [Fact]
    public async Task ChooseFromHand_Human_DeclineEmptySelection_ReturnsNull()
    {
        // An empty selection (player declines) resolves to null rather than
        // auto-picking — the optional-pickup branch human-side.
        var a = new Instant("Bolt", "R") { Owner = _alice };
        var agent = new RemoteAgent(
            _alice, cardLookup: id => id == a.InstanceId ? a : null);

        var task = ((IPlayerAgent)agent).ChooseFromHandAsync(_alice, new ICard[] { a }, BotIntent.None);

        agent.Submit(new ChoiceCommand(
            ChoiceKind.PickOne.ToString(), System.Array.Empty<Guid>())
        { PlayerId = _alice.Id });

        (await task).Should().BeNull("empty selection = decline, not auto-pick");
    }

    // -----------------------------------------------------------------------
    // CR 701.15 — reveal-and-choose prompt (Malevolent Rumble, Impulse,
    // Sleight of Hand, See the Unwritten, …).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ChooseFromRevealed_Submit_ResolvesToPickedEligibleCard()
    {
        // Reveal pile contains both a creature (eligible) and an instant
        // (not eligible). The portal must be able to ship the picked
        // creature's instance id; the wire command resolves to the
        // matching ICard.
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        var revealed = new ICard[] { bolt, bear };
        var eligible = new ICard[] { bear };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseFromRevealedAsync(
            ctx: null,
            revealed: revealed,
            eligible: eligible,
            optional: true,
            label: "Permanent to put into hand");
        agent.HasPending.Should().BeTrue();
        agent.ExpectedCommandKinds.Should().ContainSingle()
            .Which.Should().Be(typeof(ChooseFromRevealedCommand));

        agent.Submit(new ChooseFromRevealedCommand(bear.InstanceId) { PlayerId = _alice.Id });
        var picked = await task;

        picked.Should().BeSameAs(bear);
    }

    [Fact]
    public async Task ChooseFromRevealed_SubmitNull_OptionalPrompt_ResolvesToDecline()
    {
        // CR 116.1b — "you may" decline. Optional prompt + null InstanceId
        // resolves to null without falling back to the first eligible.
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseFromRevealedAsync(
            ctx: null,
            revealed: new ICard[] { bear },
            eligible: new ICard[] { bear },
            optional: true,
            label: "Permanent to put into hand");
        agent.Submit(new ChooseFromRevealedCommand(InstanceId: null) { PlayerId = _alice.Id });

        (await task).Should().BeNull();
    }

    [Fact]
    public async Task ChooseFromRevealed_InvalidInstanceId_CoercedToDecline()
    {
        // Defence: out-of-set InstanceId is logged + coerced to null
        // rather than throwing — a buggy or malicious client must never
        // crash a live match. Distinct from ChooseLibraryPickCommand which
        // throws on invalid IDs (that prompt is per-search, mid-resolve,
        // and a malformed pick there indicates a wire bug worth surfacing
        // immediately).
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseFromRevealedAsync(
            ctx: null,
            revealed: new ICard[] { bear },
            eligible: new ICard[] { bear },
            optional: true,
            label: "Permanent to put into hand");
        agent.Submit(new ChooseFromRevealedCommand(Guid.NewGuid()) { PlayerId = _alice.Id });

        (await task).Should().BeNull();
    }

    [Fact]
    public async Task ChooseFromRevealed_MandatoryPromptNullSubmit_FallsBackToFirstEligible()
    {
        // Mandatory prompt ("put one of them into your hand") with non-
        // empty eligible. A null pick is treated as agent misbehaviour,
        // not a legal decline — fall back to the first eligible so the
        // engine doesn't see a no-op on a "put one" clause.
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var elf = new Creature("Elf", "G", 1, 1) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseFromRevealedAsync(
            ctx: null,
            revealed: new ICard[] { bear, elf },
            eligible: new ICard[] { bear, elf },
            optional: false,
            label: "Card to put into hand");
        agent.Submit(new ChooseFromRevealedCommand(InstanceId: null) { PlayerId = _alice.Id });

        (await task).Should().BeSameAs(bear,
            "mandatory prompt + null pick coerces to first eligible");
    }

    [Fact]
    public async Task ChooseFromRevealed_MandatoryPromptEmptyEligible_AcceptsNullDecline()
    {
        // Even mandatory clauses can't force a pick from an empty set —
        // null is legal here (matches "if any" or "if able" wording the
        // engine surfaces when the predicate excludes every revealed
        // card).
        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseFromRevealedAsync(
            ctx: null,
            revealed: new ICard[] { bolt },
            eligible: Array.Empty<ICard>(),
            optional: false,
            label: "Permanent to put into hand");
        agent.Submit(new ChooseFromRevealedCommand(InstanceId: null) { PlayerId = _alice.Id });

        (await task).Should().BeNull();
    }

    [Fact]
    public async Task ChooseFromRevealed_PromptPayload_ShipsRevealViewWithEligibleSubset()
    {
        // The portal needs every revealed card to render the reveal pile
        // (CR 701.15 — revealed cards are publicly visible to the
        // caster's UI) plus the eligible InstanceIds so it knows which
        // cards are clickable vs muted. Verify both ship on PromptPayload.
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        _ = agent.ChooseFromRevealedAsync(
            ctx: null,
            revealed: new ICard[] { bolt, bear },
            eligible: new ICard[] { bear },
            optional: true,
            label: "Permanent to put into hand");

        agent.PendingPayload.Should().NotBeNull();
        var view = agent.PendingPayload!.RevealView;
        view.Should().NotBeNull();
        view!.Revealed.Should().HaveCount(2);
        view!.EligibleInstanceIds.Should().Contain(bear.InstanceId);
        view!.EligibleInstanceIds.Should().NotContain(bolt.InstanceId);
        view!.Optional.Should().BeTrue();
        view!.Label.Should().Be("Permanent to put into hand");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChooseFromRevealed_PromptPayload_ClearedAfterSubmit()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var agent = new RemoteAgent(_alice);

        var task = agent.ChooseFromRevealedAsync(
            ctx: null,
            revealed: new ICard[] { bear },
            eligible: new ICard[] { bear },
            optional: true,
            label: "Permanent to put into hand");
        agent.PendingPayload.Should().NotBeNull();

        agent.Submit(new ChooseFromRevealedCommand(bear.InstanceId) { PlayerId = _alice.Id });
        await task;

        agent.PendingPayload.Should().BeNull();
        agent.HasPending.Should().BeFalse();
    }
}
