using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

[Collection(nameof(StaticRegistryCollection))]
public class MulliganControllerTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public MulliganControllerTests()
    {
        // Static registries are shared between tests — start every test
        // from a clean slate so prior runs can't leak shuffle state.
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    public void Dispose()
    {
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    [Fact]
    public async Task Keep_OnFirstAsk_Leaves7CardsInHand()
    {
        SeedLibrary(20);
        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Keep);

        var ctrl = new MulliganController();
        var taken = await ctrl.RunAsync(_alice, agent, NewContext());

        taken.Should().Be(0);
        _alice.Zones.Hand.Count.Should().Be(7);
    }

    [Fact]
    public async Task OneMulligan_KeepNext_Leaves7CardsInHand_OneOnBottom()
    {
        SeedLibrary(20);
        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        agent.QueueCardsToBottom(hand => new[] { hand[0] });

        var ctrl = new MulliganController();
        var taken = await ctrl.RunAsync(_alice, agent, NewContext());

        taken.Should().Be(1);
        // London mulligan: still draw 7, but bottom N after keep.
        _alice.Zones.Hand.Count.Should().Be(6);
    }

    [Fact]
    public async Task AllMulligansKeptOnLastDraw_StopsAt7Mulligans()
    {
        SeedLibrary(60);
        var agent = new ScriptedAgent();
        for (var i = 0; i < 8; i++) agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        agent.QueueCardsToBottom(hand => hand.Take(7).ToList());

        var ctrl = new MulliganController();
        var taken = await ctrl.RunAsync(_alice, agent, NewContext());

        taken.Should().Be(7);
        _alice.Zones.Hand.Count.Should().Be(0); // 7 - 7 bottomed
    }

    [Fact]
    public async Task Mulligan_DecisionFalse_ShufflesLibraryAndRedrawsFreshSeven()
    {
        // CR 103.4 (London mulligan, 2019+): on "mulligan", the player puts
        // their hand back into their library, SHUFFLES the library, then
        // draws a new hand of seven. Without the shuffle, the redraw is
        // not actually a mulligan — it's deterministic relative to the
        // pre-mulligan top of library.
        SeedLibrary(60);
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1));

        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        // Keep with 1 mulligan → bottom 1 card. Use index 0 so the
        // assertion below only needs to look at the post-bottom hand.
        agent.QueueCardsToBottom(hand => new[] { hand[0] });

        var ctrl = new MulliganController();

        // Snapshot the top-7 BEFORE the controller runs so we can prove
        // the post-shuffle redraw is different. Also snapshot the NEXT
        // seven (positions 7..13) — those would be the redrawn hand if
        // the controller skipped the shuffle and just bubbled cards up
        // from a sequential AddCard-to-bottom + Draw-from-top, which is
        // the pre-fix buggy behaviour.
        var allOriginalIds = _alice.Zones.Library
            .GetCards()
            .Select(c => c.InstanceId)
            .ToList();
        var originalTopSeven = allOriginalIds.Take(7).ToList();
        var noShuffleRedrawWouldBe = allOriginalIds.Skip(7).Take(7).ToHashSet();

        var taken = await ctrl.RunAsync(_alice, agent, NewContext());

        // (a) Mulligan count incremented.
        taken.Should().Be(1);

        // (b) Library + hand invariant restored. After mulligan once + keep:
        //     hand = 7 drawn - 1 bottomed = 6
        //     library = 60 - 6
        _alice.Zones.Hand.Count.Should().Be(6);
        _alice.Zones.Library.Count.Should().Be(54);
        (_alice.Zones.Hand.Count + _alice.Zones.Library.Count).Should().Be(60);

        // (c) The new hand differs from the pre-mulligan top-7 — proves
        //     the library actually shuffled (with seed=1, 60 unique cards,
        //     a Fisher-Yates shuffle will not reproduce the original order).
        //     Use multiset equality on InstanceIds: if the seven were
        //     identical we'd see the same set; the shuffle should change
        //     which seven IDs ended up in hand.
        var postHandIds = _alice.Zones.Hand
            .GetCards()
            .Select(c => c.InstanceId)
            .ToHashSet();
        postHandIds.SetEquals(originalTopSeven.ToHashSet()).Should().BeFalse(
            "shuffle + redraw should yield a different set of cards than the pre-mulligan top-7");

        // And specifically NOT the "I just bubbled the cards back to the
        // bottom and redrew the next seven" sequence — that's the buggy
        // no-shuffle behaviour the user reported. After bottoming hand[0]
        // (one of the seven we just drew), 6 of 7 originally-drawn cards
        // are still in hand, so the assertion needs to compare against
        // the bug's predicted top-of-library set, not against the
        // bottomed hand itself.
        // After bottoming 1 of the 7 drawn cards, the hand holds 6
        // cards. Pre-fix (no shuffle), those 6 cards would all come from
        // the original positions 7..13 of the library — i.e.
        // postHandIds ⊂ noShuffleRedrawWouldBe. A real shuffle breaks
        // that subset relation (the new top-7 are drawn from the whole
        // shuffled library, not just the original positions 7..13).
        postHandIds.IsSubsetOf(noShuffleRedrawWouldBe).Should().BeFalse(
            "without the post-mulligan shuffle the new hand would be a subset of the original positions 7..13 of the library — a real shuffle must scramble the whole 60-card library");
    }

    [Fact]
    public async Task Mulligan_DecisionFalse_PublishesLibraryShuffledEvent()
    {
        // Belt-and-braces: the redraw path must go through the shared
        // LibraryShuffle helper so subscribers (replay log, diagnostics)
        // can observe the event the same way game-start / tutor shuffles
        // do.
        SeedLibrary(20);
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 7));
        var bus = new EventBus();
        var shuffles = new List<LibraryShuffledEvent>();
        bus.Subscribe<LibraryShuffledEvent>(e => shuffles.Add(e));
        EventBusRegistry.Set(_alice, bus);

        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        agent.QueueCardsToBottom(hand => new[] { hand[0] });

        var ctrl = new MulliganController();
        await ctrl.RunAsync(_alice, agent, NewContext());

        shuffles.Should().NotBeEmpty(
            "the per-mulligan redraw step must shuffle via LibraryShuffle so observers see the event");
        shuffles.Should().Contain(e => e.Player == _alice);
    }

    [Fact]
    public async Task Mulligan_PromptIsReinvokedAfterEachRedraw()
    {
        // The controller must call ChooseMulliganAsync once per redraw —
        // not stop after the first "Mulligan" answer.
        SeedLibrary(60);
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 3));

        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        agent.QueueCardsToBottom(hand => hand.Take(2).ToList());

        var ctrl = new MulliganController();
        var taken = await ctrl.RunAsync(_alice, agent, NewContext());

        // If the controller had short-circuited after the first mulligan
        // without re-prompting, QueueCardsToBottom (only fires on Keep)
        // would never be consumed; QueueMulligan would also not drain.
        // The successful run consuming all three mulligan answers + the
        // bottom-choice asserts the controller re-prompts each cycle.
        taken.Should().Be(2);
        _alice.Zones.Hand.Count.Should().Be(7 - 2, "London mulligan bottoms N on keep");
    }

    private void SeedLibrary(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = NamedCardFactory.Create("Mountain", _alice);
            _alice.Zones.Library.AddCard(card);
        }
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice }, _alice, 1, PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack());
}
