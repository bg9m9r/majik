using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

/// <summary>
/// Coverage for the agent-prompt targeting MVP surface:
///   * <see cref="TargetRequest.CandidateGatherer"/> resolves lazily.
///   * <see cref="PlayerAgentTargetingExtensions.ChooseTargetAsync"/> +
///     plural variant route through the agent or fall through deterministically
///     when no agent is supplied.
///   * <see cref="HeuristicBotAgent"/> intent-aware ranking picks the right
///     candidate per <see cref="BotIntent"/> bucket.
///
/// Each test mirrors the "first-candidate auto-pick" graceful-degrade posture
/// every retrofitted factory relies on when no agent is present.
/// </summary>
public class AgentPromptTargetingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------- TargetRequest.ResolveCandidates / WithCandidates ----------------

    [Fact]
    public void ResolveCandidates_NoGatherer_ReturnsLegalCandidates()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var req = new TargetRequest("any", 1, 1, new[] { (object)bear });

        var resolved = req.ResolveCandidates(NewContext());

        resolved.Should().BeSameAs(req.LegalCandidates);
    }

    [Fact]
    public void ResolveCandidates_GathererSupplies_ReturnsMergedList()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var wolf = new Creature("Wolf", "2G", 3, 3) { Owner = _bob };
        var req = new TargetRequest(
            "any", 1, 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: _ => new object[] { bear, wolf });

        var resolved = req.ResolveCandidates(NewContext());

        resolved.Should().HaveCount(2);
        resolved.Should().Contain(bear);
        resolved.Should().Contain(wolf);
    }

    [Fact]
    public void WithCandidates_ReplacesLegalCandidates_PreservesIntent()
    {
        var req = new TargetRequest(
            "any", 1, 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Removal);

        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var replaced = req.WithCandidates(new object[] { bear });

        replaced.LegalCandidates.Should().ContainSingle().Which.Should().BeSameAs(bear);
        replaced.Intent.Should().Be(BotIntent.Removal);
        replaced.Description.Should().Be("any");
    }

    // ---------------- PlayerAgentTargetingExtensions ----------------

    [Fact]
    public async Task ChooseTargetAsync_NullAgent_ReturnsFirstCandidate()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var wolf = new Creature("Wolf", "2G", 3, 3) { Owner = _bob };

        IPlayerAgent? agent = null;
        var picked = await agent.ChooseTargetAsync(
            NewContext(), "any", new object[] { bear, wolf }, BotIntent.Removal);

        picked.Should().BeSameAs(bear);
    }

    [Fact]
    public async Task ChooseTargetAsync_EmptyCandidates_ReturnsNull()
    {
        IPlayerAgent? agent = null;
        var picked = await agent.ChooseTargetAsync(
            NewContext(), "any", Array.Empty<object>());

        picked.Should().BeNull();
    }

    [Fact]
    public async Task ChooseTargetAsync_RoutesThroughAgent()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var wolf = new Creature("Wolf", "2G", 3, 3) { Owner = _bob };

        var scripted = new ScriptedAgent();
        scripted.QueueTargets(new object[] { wolf });

        var picked = await ((IPlayerAgent?)scripted).ChooseTargetAsync(
            NewContext(), "any", new object[] { bear, wolf });

        picked.Should().BeSameAs(wolf);
    }

    [Fact]
    public async Task ChooseTargetsAsync_Plural_NullAgent_ReturnsFirstN()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var wolf = new Creature("Wolf", "2G", 3, 3) { Owner = _bob };
        var hydra = new Creature("Hydra", "3G", 4, 4) { Owner = _bob };

        IPlayerAgent? agent = null;
        var picked = await agent.ChooseTargetsAsync(
            NewContext(), "any", new object[] { bear, wolf, hydra }, count: 2);

        picked.Should().HaveCount(2);
        picked.Should().Equal(bear, wolf);
    }

    // ---------------- HeuristicBotAgent intent-aware ranking ----------------

    [Fact]
    public async Task Heuristic_RemovalIntent_PicksBiggestOpponentCreature()
    {
        var smallOpp = new Creature("Squire", "W", 1, 2)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var bigOpp = new Creature("Dragon", "4R", 6, 6)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var mine = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var bot = new HeuristicBotAgent();
        var req = new TargetRequest(
            "target creature", 1, 1,
            new object[] { smallOpp, bigOpp, mine },
            BotIntent.Removal);

        var picked = await bot.ChooseTargetsAsync(NewContext(), req);

        picked.Should().ContainSingle().Which.Should().BeSameAs(bigOpp);
    }

    [Fact]
    public async Task Heuristic_BuffIntent_PicksOwnCreature()
    {
        var smallOpp = new Creature("Squire", "W", 1, 2)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var mine = new Creature("Bear", "1G", 2, 2)
            { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        var bot = new HeuristicBotAgent();
        var req = new TargetRequest(
            "target creature you control", 1, 1,
            new object[] { smallOpp, mine },
            BotIntent.Buff);

        var picked = await bot.ChooseTargetsAsync(NewContext(), req);

        picked.Should().ContainSingle().Which.Should().BeSameAs(mine);
    }

    [Fact]
    public async Task Heuristic_BounceIntent_PicksMostExpensiveOpponentPermanent()
    {
        var cheapOpp = new Creature("Squire", "W", 1, 2)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var expensiveOpp = new Creature("Titan", "4WWW", 6, 6)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };

        var bot = new HeuristicBotAgent();
        var req = new TargetRequest(
            "target creature", 1, 1,
            new object[] { cheapOpp, expensiveOpp },
            BotIntent.Bounce);

        var picked = await bot.ChooseTargetsAsync(NewContext(), req);

        picked.Should().ContainSingle().Which.Should().BeSameAs(expensiveOpp);
    }

    [Fact]
    public async Task Heuristic_BurnIntent_PrefersOpponentBigCreatureWhenPresent()
    {
        var smallOpp = new Creature("Squire", "W", 1, 2)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var bigOpp = new Creature("Dragon", "4R", 6, 6)
            { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };

        var bot = new HeuristicBotAgent();
        var req = new TargetRequest(
            "any target", 1, 1,
            new object[] { smallOpp, bigOpp },
            BotIntent.Burn);

        var picked = await bot.ChooseTargetsAsync(NewContext(), req);

        picked.Should().ContainSingle().Which.Should().BeSameAs(bigOpp);
    }

    [Fact]
    public async Task Heuristic_PlayerTarget_BurnPicksLowestLifeOpponent()
    {
        var lowLifeBob = new Player("Bob", 3);
        var ctx = new GameContext(
            _alice, new[] { _alice, lowLifeBob }, _alice, 1,
            PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack());

        var bot = new HeuristicBotAgent();
        var req = new TargetRequest(
            "any target", 1, 1,
            new object[] { _alice, lowLifeBob },
            BotIntent.Burn);

        var picked = await bot.ChooseTargetsAsync(ctx, req);

        picked.Should().ContainSingle().Which.Should().BeSameAs(lowLifeBob);
    }

    // ---------------- Pilot-factory regression: empty LegalCandidates path ----------------

    /// <summary>
    /// The 10 retrofitted factories ship a CandidateGatherer instead of a
    /// pre-populated LegalCandidates list. This regression verifies the
    /// engine's prompt sites (covered by SpellCastFlow / AbilityActivationFlow
    /// / TriggerManager integration tests) still see a non-empty pool when
    /// candidates exist on the live board. Direct unit: a TargetRequest with
    /// a gatherer + empty LegalCandidates returns the gathered pool.
    /// </summary>
    [Fact]
    public void RetrofittedFactoryShape_GathererPopulatesEmptyLegalCandidates()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };

        // Mirrors the shape every pilot factory uses:
        // empty LegalCandidates + non-null gatherer.
        var req = new TargetRequest(
            "target creature", 1, 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Removal,
            CandidateGatherer: _ => new object[] { bear });

        var resolved = req.ResolveCandidates(NewContext());

        resolved.Should().ContainSingle().Which.Should().BeSameAs(bear);
    }

    private GameContext NewContext()
    {
        var stack = new Majik.Core.Stack.Stack();
        return new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, stack);
    }
}
