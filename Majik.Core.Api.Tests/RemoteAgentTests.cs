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
}
