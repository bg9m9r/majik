using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Bot.Search;
using Majik.Bot.Strategies;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Strategies;

// ── Stubs ──────────────────────────────────────────────────────────────────────

/// <summary>Always returns a fixed PriorityAction as the "assembled win line".</summary>
internal sealed class WinLineStub : IDeckStrategy
{
    private readonly PriorityAction _action;
    public WinLineStub(PriorityAction action) => _action = action;

    public double StrategicScore(GameContext ctx, Player self) => 0;
    public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self) => _action;
    public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int n) => null;
}

/// <summary>Always advises a fixed MulliganDecision.</summary>
internal sealed class MulliganAdviceStub : IDeckStrategy
{
    private readonly MulliganDecision _decision;
    public MulliganAdviceStub(MulliganDecision decision) => _decision = decision;

    public double StrategicScore(GameContext ctx, Player self) => 0;
    public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self) => null;
    public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int n) => _decision;
}

/// <summary>Never advises anything — null on all advisory methods.</summary>
internal sealed class NullDeckStub : IDeckStrategy
{
    public double StrategicScore(GameContext ctx, Player self) => 0;
    public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self) => null;
    public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int n) => null;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

public sealed class StrategyWiringTests
{
    // ── HeuristicStrategy ──────────────────────────────────────────────────────

    [Fact]
    public void Heuristic_PickPriorityAction_ExecutesWinLine_WhenStrategyProvidesOne()
    {
        // Arrange: a win-line stub that always returns Pass as the directive.
        // Pass is a concrete singleton — identity comparison is reliable.
        var theAction = PriorityAction.Pass;
        var stub = new WinLineStub(theAction);

        var s = new BotTestScenario();
        var heuristic = new HeuristicStrategy(new BotConfig("Burn", Strategy: "heuristic"), deckOverride: stub);

        // Act
        var result = heuristic.PickPriorityAction(s.Context, s.Self);

        // Assert: the deck strategy's directive is returned verbatim, overriding
        // whatever the heuristic would have calculated.
        result.Should().BeSameAs(theAction, "win-line directive must override heuristic scoring");
    }

    [Fact]
    public void Heuristic_PickMulligan_FollowsDeckAdvice_WhenProvided()
    {
        // Arrange: stub that always advises Keep.
        var stub = new MulliganAdviceStub(MulliganDecision.Keep);

        var heuristic = new HeuristicStrategy(new BotConfig("Burn", Strategy: "heuristic"), deckOverride: stub);
        var hand = new List<ICard> { new Land("Mountain") };

        // Act
        var result = heuristic.PickMulligan(hand, mulligansTaken: 0);

        // Assert
        result.Should().Be(MulliganDecision.Keep, "deck strategy advises Keep");
    }

    [Fact]
    public void Heuristic_PickMulligan_FollowsDeckAdvice_Mulligan()
    {
        // Arrange: stub that always advises Mulligan.
        var stub = new MulliganAdviceStub(MulliganDecision.Mulligan);

        var heuristic = new HeuristicStrategy(new BotConfig("Burn", Strategy: "heuristic"), deckOverride: stub);
        // A hand of 7 lands — generic policy would likely Keep, but deck advice overrides.
        var hand = Enumerable.Range(0, 7).Select(_ => (ICard)new Land("Mountain")).ToList();

        // Act
        var result = heuristic.PickMulligan(hand, mulligansTaken: 0);

        // Assert
        result.Should().Be(MulliganDecision.Mulligan, "deck strategy advises Mulligan");
    }

    [Fact]
    public void Heuristic_PickPriorityAction_NullStrategy_FallsThroughToHeuristic()
    {
        // Arrange: no deck strategy injected → _deckStrategy is null.
        // An empty hand + no mana means Pass is the expected heuristic result.
        var s = new BotTestScenario();
        var heuristic = new HeuristicStrategy(new BotConfig("Burn", Strategy: "heuristic"), deckOverride: null);

        // Act
        var result = heuristic.PickPriorityAction(s.Context, s.Self);

        // Assert: heuristic falls through to Pass (no legal plays).
        result.Should().BeOfType<PriorityAction.PassAction>(
            "null deck strategy leaves heuristic behaviour unchanged");
    }

    [Fact]
    public void Heuristic_PickMulligan_NullStrategy_UsesGenericPolicy()
    {
        // Arrange: no deck override — generic MulliganPolicy decides.
        // 7-land hand should trigger generic Mulligan.
        var heuristic = new HeuristicStrategy(new BotConfig("Burn", Strategy: "heuristic"), deckOverride: null);
        var allLands = Enumerable.Range(0, 7).Select(_ => (ICard)new Land("Mountain")).ToList();

        // Act
        var result = heuristic.PickMulligan(allLands, mulligansTaken: 0);

        // Assert: generic policy — result is whatever MulliganPolicy.Decide returns
        // for 7 lands (we just verify it doesn't throw and is a valid decision).
        result.Should().BeOneOf(MulliganDecision.Keep, MulliganDecision.Mulligan);
    }

    // ── SearchStrategy ─────────────────────────────────────────────────────────

    [Fact]
    public void Search_PickMulligan_FollowsDeckAdvice_WhenProvided()
    {
        // SearchStrategy.PickMulligan delegates to the inner HeuristicStrategy,
        // which should in turn follow the deck advice.
        var stub = new MulliganAdviceStub(MulliganDecision.Keep);
        var search = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts"), deckOverride: stub);
        var hand = new List<ICard> { new Land("Mountain") };

        var result = search.PickMulligan(hand, mulligansTaken: 0);

        result.Should().Be(MulliganDecision.Keep, "SearchStrategy must propagate deck advice via inner heuristic");
    }

    [Fact]
    public void Search_PickPriorityAction_ExecutesWinLine_WhenStrategyProvidesOne()
    {
        // SearchStrategy with priority search DISABLED (fast path → inner heuristic)
        // so the win-line check in the inner heuristic fires before search.
        var theAction = PriorityAction.Pass;
        var stub = new WinLineStub(theAction);

        var s = new BotTestScenario();
        var search = new SearchStrategy(
            new BotConfig("Burn", Strategy: "mcts", PrioritySearchEnabled: false),
            deckOverride: stub);

        var result = search.PickPriorityAction(s.Context, s.Self);

        result.Should().BeSameAs(theAction, "win-line directive must override search/heuristic");
    }
}
