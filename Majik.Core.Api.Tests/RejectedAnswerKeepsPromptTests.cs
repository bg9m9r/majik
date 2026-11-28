using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// A rejected answer must never wedge the game. Before the fix
/// <see cref="RemoteAgent.Submit"/> cleared the pending prompt BEFORE
/// validating the command, so any check failing during resolution (unknown
/// instance id, wrong bottom count, trigger-order mismatch, …) threw with the
/// engine still awaiting a prompt that no longer existed. Now: the invalid
/// answer is rejected with a reason, the SAME prompt stays pending (same kinds,
/// same payload, <see cref="RemoteAgent.PromptRequested"/> not re-fired), and a
/// later valid answer resumes the engine. One test per command kind that
/// validates its content.
/// </summary>
public class RejectedAnswerKeepsPromptTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Dictionary<Guid, ICard> _cards = new();
    private readonly RemoteAgent _agent;
    private int _promptsRaised;

    public RejectedAnswerKeepsPromptTests()
    {
        _agent = new RemoteAgent(
            _alice,
            cardLookup: id => _cards.TryGetValue(id, out var c) ? c : null,
            playerLookup: id => id == _alice.Id ? _alice : id == _bob.Id ? _bob : null);
        _agent.PromptRequested += _ => _promptsRaised++;
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 2, StepStateType.PreCombatMain, new Majik.Core.Stack.Stack());

    private T Put<T>(T card, Player owner, ZoneType zone) where T : Card
    {
        card.SetOwner(owner);
        card.SetController(owner);
        switch (zone)
        {
            case ZoneType.Hand: owner.Zones.Hand.AddCard(card); break;
            case ZoneType.Battlefield: owner.Zones.Battlefield.AddCard(card); break;
            case ZoneType.Library: owner.Zones.Library.AddCard(card); break;
        }
        card.SetZone(zone);
        _cards[card.InstanceId] = card;
        return card;
    }

    private T Cmd<T>(T command) where T : GameCommand => command with { PlayerId = _alice.Id };

    /// <summary>Submit <paramref name="invalid"/>; assert it is rejected with a
    /// reason matching <paramref name="reason"/> and that the prompt is
    /// untouched; then submit <paramref name="valid"/> and return the result.</summary>
    private async Task<TResult> RejectThenAccept<TResult>(
        Task<TResult> pending, GameCommand invalid, string reason, GameCommand valid)
    {
        _agent.HasPending.Should().BeTrue("precondition: a prompt is pending");
        var kinds = _agent.ExpectedCommandKinds!.ToList();
        var payload = _agent.PendingPayload;
        var raised = _promptsRaised;

        var act = () => _agent.Submit(invalid);

        act.Should().Throw<InvalidOperationException>().WithMessage(reason);
        _agent.HasPending.Should().BeTrue("a rejected answer leaves the prompt pending");
        _agent.ExpectedCommandKinds.Should().Equal(kinds);
        _agent.PendingPayload.Should().BeSameAs(payload);
        _promptsRaised.Should().Be(raised, "the same prompt stays pending; it is not raised again");
        pending.IsCompleted.Should().BeFalse("the engine is still waiting for an answer");

        _agent.Submit(valid);
        (await Task.WhenAny(pending, Task.Delay(5000))).Should().BeSameAs(pending, "a valid answer resumes the engine");
        _agent.HasPending.Should().BeFalse();
        return await pending;
    }

    [Fact]
    public async Task PlayLand_UnknownInstance_Rejected_ThenValid()
    {
        var land = Put(new Land("Forest"), _alice, ZoneType.Hand);
        var task = _agent.ChoosePriorityActionAsync(Ctx());

        var action = await RejectThenAccept(task,
            Cmd(new PlayLandCommand(Guid.NewGuid())), "No card found for instance*",
            Cmd(new PlayLandCommand(land.InstanceId)));

        action.Should().BeOfType<PriorityAction.PlayLand>();
    }

    [Fact]
    public async Task CastSpell_UnknownCardOrTarget_Rejected_ThenValid()
    {
        var bolt = Put(new Instant("Lightning Bolt", "{R}"), _alice, ZoneType.Hand);
        var task = _agent.ChoosePriorityActionAsync(Ctx());

        var action = await RejectThenAccept(task,
            Cmd(new CastSpellCommand(bolt.InstanceId, new[] { Guid.NewGuid() }, null, null)), "No card found for instance*",
            Cmd(new CastSpellCommand(bolt.InstanceId, Array.Empty<Guid>(), null, null)));

        action.Should().BeOfType<PriorityAction.CastSpell>();
    }

    [Fact]
    public async Task ChooseMana_UnknownSource_Rejected_ThenValid()
    {
        var forest = Put(new Land("Forest"), _alice, ZoneType.Battlefield);
        var task = _agent.ChooseManaSourcesAsync(Ctx(), ManaCost.Parse("{G}"));

        var payment = await RejectThenAccept(task,
            Cmd(new ChooseManaCommand(new[] { forest.InstanceId, Guid.NewGuid() })), "No card found for instance*",
            Cmd(new ChooseManaCommand(new[] { forest.InstanceId })));

        payment.Sources.Should().ContainSingle();
    }

    [Fact]
    public async Task ActivateManaAbility_WrongColour_Rejected_ThenValid()
    {
        var forest = Put(new Land("Forest"), _alice, ZoneType.Battlefield);
        forest.AddAbility(new ManaAbility(forest, _alice, ManaCost.Parse("G")));
        var task = _agent.ChoosePriorityActionAsync(Ctx());

        var action = await RejectThenAccept(task,
            Cmd(new ActivateManaAbilityCommand(forest.InstanceId, "U")), "*no mana ability producing*",
            Cmd(new ActivateManaAbilityCommand(forest.InstanceId, "G")));

        action.Should().BeOfType<PriorityAction.ActivateManaAbility>();
    }

    [Fact]
    public async Task ActivateAbility_UnknownAbility_Rejected_ThenValid()
    {
        var pinger = Put(new Creature("Prodigal Pyromancer", "{2}{R}", 1, 1), _alice, ZoneType.Battlefield);
        var ping = new ActivatedAbility(pinger, _alice);
        pinger.AddAbility(ping);
        var task = _agent.ChoosePriorityActionAsync(Ctx());

        var action = await RejectThenAccept(task,
            Cmd(new ActivateAbilityCommand(pinger.InstanceId, Guid.NewGuid())), "*no non-mana activated ability with id*",
            Cmd(new ActivateAbilityCommand(pinger.InstanceId, ping.Id)));

        action.Should().BeOfType<PriorityAction.ActivateAbility>();
    }

    [Fact]
    public async Task ActivateLoyaltyAbility_OpponentsPermanent_Rejected_ThenValid()
    {
        var mine = Put(new Planeswalker("Chandra", "{2}{R}{R}", 4), _alice, ZoneType.Battlefield);
        var plus = new LoyaltyAbility(mine, +1, () => { });
        mine.AddAbility(plus);
        var theirs = Put(new Planeswalker("Jace", "{2}{U}{U}", 3), _bob, ZoneType.Battlefield);
        var theirPlus = new LoyaltyAbility(theirs, +1, () => { });
        theirs.AddAbility(theirPlus);
        var task = _agent.ChoosePriorityActionAsync(Ctx());

        var action = await RejectThenAccept(task,
            Cmd(new ActivateLoyaltyAbilityCommand(theirs.InstanceId, theirPlus.Id)), "*does not control permanent*",
            Cmd(new ActivateLoyaltyAbilityCommand(mine.InstanceId, plus.Id)));

        action.Should().BeOfType<PriorityAction.ActivateLoyaltyAbility>();
    }

    [Fact]
    public async Task OrderTriggers_UnknownMissingOrDuplicatedId_Rejected_ThenValid()
    {
        var bear = Put(new Creature("Bear", "{1}{G}", 2, 2), _alice, ZoneType.Battlefield);
        var a = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        var b = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        var task = _agent.OrderTriggersAsync(Ctx(), new ITriggeredAbility[] { a, b });

        FluentActions.Invoking(() => _agent.Submit(Cmd(new OrderTriggersCommand(new[] { a.Id }))))
            .Should().Throw<InvalidOperationException>().WithMessage("*listed 1 triggers but engine expected 2*");
        FluentActions.Invoking(() => _agent.Submit(Cmd(new OrderTriggersCommand(new[] { a.Id, a.Id }))))
            .Should().Throw<InvalidOperationException>().WithMessage("*more than once*");

        var ordered = await RejectThenAccept(task,
            Cmd(new OrderTriggersCommand(new[] { b.Id, Guid.NewGuid() })), "*unknown stack object*",
            Cmd(new OrderTriggersCommand(new[] { b.Id, a.Id })));

        ordered.Should().Equal(b, a);
    }

    [Fact]
    public async Task DeclareAttackers_NonCreatureOrBadDefender_Rejected_ThenValid()
    {
        var bear = Put(new Creature("Bear", "{1}{G}", 2, 2), _alice, ZoneType.Battlefield);
        var land = Put(new Land("Forest"), _alice, ZoneType.Battlefield);
        var task = _agent.DeclareAttackersAsync(Ctx(), new Permanent[] { bear });

        FluentActions.Invoking(() => _agent.Submit(Cmd(new DeclareAttackersCommand(
                new[] { new AttackerDeclarationDto(land.InstanceId, _bob.Id) }))))
            .Should().Throw<InvalidOperationException>().WithMessage("*not an eligible attacker*");

        var plan = await RejectThenAccept(task,
            Cmd(new DeclareAttackersCommand(new[] { new AttackerDeclarationDto(bear.InstanceId, Guid.NewGuid()) })),
            "*neither a known player nor a Planeswalker*",
            Cmd(new DeclareAttackersCommand(new[] { new AttackerDeclarationDto(bear.InstanceId, _bob.Id) })));

        plan.Attackers.Should().ContainSingle();
    }

    [Fact]
    public async Task DeclareBlockers_UnknownAttacker_Rejected_ThenValid()
    {
        var attacker = Put(new Creature("Goblin", "{R}", 1, 1), _bob, ZoneType.Battlefield);
        var wall = Put(new Creature("Wall", "{1}{W}", 0, 4), _alice, ZoneType.Battlefield);
        var task = _agent.DeclareBlockersAsync(Ctx(), new Permanent[] { attacker }, new Permanent[] { wall });

        var plan = await RejectThenAccept(task,
            Cmd(new DeclareBlockersCommand(new[] { new BlockerDeclarationDto(wall.InstanceId, Guid.NewGuid()) })),
            "No card found for instance*",
            Cmd(new DeclareBlockersCommand(new[] { new BlockerDeclarationDto(wall.InstanceId, attacker.InstanceId) })));

        plan.Blockers.Should().ContainSingle();
    }

    [Fact]
    public async Task ChooseSurveil_IncompletePartition_Rejected_ThenValid()
    {
        var top = Put(new Land("Island"), _alice, ZoneType.Library);
        var next = Put(new Land("Swamp"), _alice, ZoneType.Library);
        var task = _agent.ChooseSurveilDecisionAsync(Ctx(), new ICard[] { top, next });

        var decision = await RejectThenAccept(task,
            Cmd(new ChooseSurveilCommand(new[] { top.InstanceId }, Array.Empty<Guid>())), "*partitioned 1 cards but engine peeked 2*",
            Cmd(new ChooseSurveilCommand(new[] { top.InstanceId }, new[] { next.InstanceId })));

        decision.ToGraveyard.Should().Equal(top);
    }

    [Fact]
    public async Task ChooseScry_UnknownCard_Rejected_ThenValid()
    {
        var top = Put(new Land("Island"), _alice, ZoneType.Library);
        var task = _agent.ChooseScryDecisionAsync(Ctx(), new ICard[] { top });

        var decision = await RejectThenAccept(task,
            Cmd(new ChooseScryCommand(new[] { Guid.NewGuid() }, Array.Empty<Guid>())), "*references unknown instance*",
            Cmd(new ChooseScryCommand(Array.Empty<Guid>(), new[] { top.InstanceId })));

        decision.TopOrder.Should().Equal(top);
    }

    [Fact]
    public async Task ChooseLibraryPick_NotACandidate_Rejected_ThenValid()
    {
        var forest = Put(new Land("Forest"), _alice, ZoneType.Library);
        var island = Put(new Land("Island"), _alice, ZoneType.Library);
        var task = _agent.ChooseLibraryPickAsync(Ctx(), new ICard[] { forest }, "a Forest card");

        var picked = await RejectThenAccept(task,
            Cmd(new ChooseLibraryPickCommand(island.InstanceId)), "*is not in the offered candidate list*",
            Cmd(new ChooseLibraryPickCommand(forest.InstanceId)));

        picked.Should().BeSameAs(forest);
    }

    [Fact]
    public async Task ChooseCardsToBottom_WrongCountOrNotInHand_Rejected_ThenValid()
    {
        // CR 103.4 — exactly N cards (N = mulligans taken) from hand.
        var a = Put(new Land("Forest"), _alice, ZoneType.Hand);
        var b = Put(new Land("Island"), _alice, ZoneType.Hand);
        var inLibrary = Put(new Land("Swamp"), _alice, ZoneType.Library);
        var task = _agent.ChooseCardsToBottomAsync(Ctx(), new ICard[] { a, b }, countToBottom: 1);

        FluentActions.Invoking(() => _agent.Submit(Cmd(new ChooseCardsToBottomCommand(new[] { a.InstanceId, b.InstanceId }))))
            .Should().Throw<InvalidOperationException>().WithMessage("*must put exactly 1 on the bottom*");

        var bottomed = await RejectThenAccept(task,
            Cmd(new ChooseCardsToBottomCommand(new[] { inLibrary.InstanceId })), "*is not in the player's hand*",
            Cmd(new ChooseCardsToBottomCommand(new[] { b.InstanceId })));

        bottomed.Should().Equal(b);
    }

    [Fact]
    public async Task Choice_WrongKind_Rejected_ThenValid()
    {
        var bear = Put(new Creature("Bear", "{1}{G}", 2, 2), _alice, ZoneType.Battlefield);
        var task = _agent.ChooseAsync(Ctx(), new ChoiceRequest(
            ChoiceKind.PickOne, "Sacrifice a creature", 1, 1, new object[] { bear }));

        var chosen = await RejectThenAccept(task,
            Cmd(new ChoiceCommand("YesNo", Array.Empty<Guid>(), YesNo: true)), "*does not match the pending*",
            Cmd(new ChoiceCommand("PickOne", new[] { bear.InstanceId })));

        chosen.Should().Equal(bear);
    }

    [Fact]
    public async Task FullGame_RejectedBottomCount_DoesNotWedge_ValidAnswerResumes()
    {
        // End-to-end through GameFacade: a London mulligan, then a wrong-count
        // bottom answer. The game must still be waiting on the same prompt and
        // a correct answer must move it on to the opponent's mulligan.
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Mountain")).ToList());
        var prompts = new System.Collections.Concurrent.ConcurrentQueue<Majik.Core.Api.Dtos.PromptDto>();
        using var sub = facade.SubscribePrompts(prompts.Enqueue);
        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 2, rng: new Majik.Core.Random.GameRandom(1));

        await facade.SubmitAsync(new MulliganCommand(Keep: false) { PlayerId = facade.Alice.Id });
        // Keep every following mulligan decision until Alice is asked to bottom.
        // Read the pending seat off the agents (the prompt observers may run a
        // moment after SubmitAsync returns).
        var alice = Seat(facade, "_aliceAgent");
        var bob = Seat(facade, "_bobAgent");
        for (var i = 0; i < 6 && !(alice.ExpectedCommandKinds?.Contains(typeof(ChooseCardsToBottomCommand)) ?? false); i++)
        {
            var seat = alice.HasPending ? facade.Alice : facade.Bob;
            await facade.SubmitAsync(new MulliganCommand(Keep: true) { PlayerId = seat.Id });
        }
        alice.ExpectedCommandKinds.Should().Equal(typeof(ChooseCardsToBottomCommand));
        bob.HasPending.Should().BeFalse();
        var hand = facade.Alice.Zones.Hand.GetCards().ToList();
        var raised = 0;
        alice.PromptRequested += _ => raised++;

        var bad = () => facade.SubmitAsync(new ChooseCardsToBottomCommand(Array.Empty<Guid>()) { PlayerId = facade.Alice.Id });
        await bad.Should().ThrowAsync<InvalidOperationException>().WithMessage("*must put exactly 1 on the bottom*");
        raised.Should().Be(0, "no new prompt was raised");
        alice.ExpectedCommandKinds.Should().ContainSingle().Which.Should().Be(typeof(ChooseCardsToBottomCommand), "the same prompt is still pending");

        await facade.SubmitAsync(new ChooseCardsToBottomCommand(new[] { hand[0].InstanceId }) { PlayerId = facade.Alice.Id });
        (alice.HasPending || bob.HasPending).Should().BeTrue("the game moved on to its next decision");
        (alice.ExpectedCommandKinds?.Contains(typeof(ChooseCardsToBottomCommand)) ?? false).Should().BeFalse();
        facade.Alice.Zones.Hand.ContainsCard(hand[0]).Should().BeFalse("CR 103.4 — the chosen card left the hand");
        facade.Alice.Zones.Library.ContainsCard(hand[0]).Should().BeTrue("CR 103.4 — it went to the library bottom");
        facade.FullGameTask!.IsFaulted.Should().BeFalse();
    }

    private static RemoteAgent Seat(GameFacade facade, string field) =>
        (RemoteAgent)typeof(GameFacade)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;
}
