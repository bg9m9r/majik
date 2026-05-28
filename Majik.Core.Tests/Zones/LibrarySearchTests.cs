using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Zones;

/// <summary>
/// CR 701.19a / CR 701.20a — coverage for the shared "search your library"
/// helper. Two invariants matter:
///   1) When a human agent is registered, the agent is prompted EVEN
///      with an empty candidate list — the silent-no-op behaviour that
///      broke Green Sun's Zenith into a deck containing zero green
///      creatures is exactly what this helper exists to prevent.
///   2) The library is shuffled afterward whether or not a card was
///      actually moved (CR 701.20a: a search effect performs one shuffle
///      whether or not a card was found).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class LibrarySearchTests : IDisposable
{
    private readonly Player _alice;

    public LibrarySearchTests()
    {
        AgentRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 0));
        _alice = new Player("Alice");
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    // Minimal IPlayerAgent that only implements the choice we care about.
    // Throws on anything else so a test that accidentally exercises an
    // unrelated agent surface fails loudly rather than silently passing.
    private sealed class RecordingAgent : IPlayerAgent
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<ICard>? LastCandidates { get; private set; }
        public string? LastLabel { get; private set; }
        public ICard? PickToReturn { get; init; }

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
        {
            CallCount++;
            LastCandidates = candidates;
            LastLabel = kindLabel;
            return Task.FromResult(PickToReturn);
        }

        // All other IPlayerAgent surface area — throw if unexpectedly invoked.
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Majik.Core.Cards.Creature> eligibleAttackers, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Majik.Core.Cards.Creature> attackers, IReadOnlyList<Majik.Core.Cards.Creature> eligibleBlockers, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotImplementedException();
    }

    [Fact]
    public void PromptAndShuffle_EmptyCandidates_StillPromptsAgent()
    {
        var agent = new RecordingAgent { PickToReturn = null };
        AgentRegistry.Set(_alice, agent);

        var result = LibrarySearch.PromptAndShuffle(
            _alice,
            candidates: Array.Empty<ICard>(),
            kindLabel: "green creature card with mana value 3 or less",
            shuffleReason: "test-tutor");

        // Even though no candidates matched, the agent was prompted so
        // the player can see (in the UI) that the search yielded nothing.
        agent.CallCount.Should().Be(1);
        agent.LastCandidates.Should().BeEmpty();
        agent.LastLabel.Should().Be("green creature card with mana value 3 or less");
        result.Should().BeNull();
    }

    [Fact]
    public void PromptAndShuffle_EmptyCandidates_ShufflesLibrary()
    {
        var agent = new RecordingAgent { PickToReturn = null };
        AgentRegistry.Set(_alice, agent);

        var observed = new List<LibraryShuffledEvent>();
        var bus = new EventBus();
        bus.Subscribe<LibraryShuffledEvent>(observed.Add);
        EventBusRegistry.Set(_alice, bus);

        _ = LibrarySearch.PromptAndShuffle(
            _alice,
            candidates: Array.Empty<ICard>(),
            kindLabel: "green creature card",
            shuffleReason: "green-suns-zenith-empty");

        // CR 701.20a — the search still happened, so the library still
        // shuffles even on a zero-candidates result.
        observed.Should().HaveCount(1);
        observed[0].Reason.Should().Be("green-suns-zenith-empty");
    }

    [Fact]
    public void PromptAndShuffle_NoAgentRegistered_EmptyCandidates_ReturnsNullAndShuffles()
    {
        // No agent registered — tests that exercise factories without
        // setting up an IPlayerAgent should still see a sensible result.
        var observed = new List<LibraryShuffledEvent>();
        var bus = new EventBus();
        bus.Subscribe<LibraryShuffledEvent>(observed.Add);
        EventBusRegistry.Set(_alice, bus);

        var result = LibrarySearch.PromptAndShuffle(
            _alice,
            candidates: Array.Empty<ICard>(),
            kindLabel: "anything",
            shuffleReason: "no-agent-empty");

        result.Should().BeNull();
        observed.Should().HaveCount(1);
    }

    [Fact]
    public void PromptAndShuffle_NoAgent_NonEmptyCandidates_ReturnsFirstAndShuffles()
    {
        var elf = new Creature("Llanowar Elves", "G", 1, 1);
        var bop = new Creature("Birds of Paradise", "G", 0, 1);

        var observed = new List<LibraryShuffledEvent>();
        var bus = new EventBus();
        bus.Subscribe<LibraryShuffledEvent>(observed.Add);
        EventBusRegistry.Set(_alice, bus);

        var result = LibrarySearch.PromptAndShuffle(
            _alice,
            candidates: new ICard[] { elf, bop },
            kindLabel: "green creature",
            shuffleReason: "no-agent-nonempty");

        result.Should().BeSameAs(elf);
        observed.Should().HaveCount(1);
    }

    [Fact]
    public void PromptAndShuffle_AgentPicksCard_ReturnsThatPick()
    {
        var elf = new Creature("Llanowar Elves", "G", 1, 1);
        var bop = new Creature("Birds of Paradise", "G", 0, 1);
        var agent = new RecordingAgent { PickToReturn = bop };
        AgentRegistry.Set(_alice, agent);

        var result = LibrarySearch.PromptAndShuffle(
            _alice,
            candidates: new ICard[] { elf, bop },
            kindLabel: "green creature",
            shuffleReason: "agent-picks");

        result.Should().BeSameAs(bop);
        agent.CallCount.Should().Be(1);
    }

    [Fact]
    public void PromptAndShuffle_AgentDeclines_ReturnsNullAndStillShuffles()
    {
        var elf = new Creature("Llanowar Elves", "G", 1, 1);
        var agent = new RecordingAgent { PickToReturn = null };
        AgentRegistry.Set(_alice, agent);

        var observed = new List<LibraryShuffledEvent>();
        var bus = new EventBus();
        bus.Subscribe<LibraryShuffledEvent>(observed.Add);
        EventBusRegistry.Set(_alice, bus);

        var result = LibrarySearch.PromptAndShuffle(
            _alice,
            candidates: new ICard[] { elf },
            kindLabel: "creature",
            shuffleReason: "agent-declines");

        result.Should().BeNull();
        // CR 701.20a — the search happened (even though the agent
        // declined to find), so the library still shuffles.
        observed.Should().HaveCount(1);
    }

    [Fact]
    public void PromptOnly_DoesNotShuffle()
    {
        var agent = new RecordingAgent { PickToReturn = null };
        AgentRegistry.Set(_alice, agent);

        var observed = new List<LibraryShuffledEvent>();
        var bus = new EventBus();
        bus.Subscribe<LibraryShuffledEvent>(observed.Add);
        EventBusRegistry.Set(_alice, bus);

        var result = LibrarySearch.PromptOnly(
            _alice,
            candidates: Array.Empty<ICard>(),
            kindLabel: "anything");

        // PromptOnly is for multi-pick effects (Scapeshift, Cultivate) that
        // shuffle once at the end. It must still prompt the agent on empty
        // candidates but must NOT shuffle.
        agent.CallCount.Should().Be(1);
        result.Should().BeNull();
        observed.Should().BeEmpty();
    }
}
