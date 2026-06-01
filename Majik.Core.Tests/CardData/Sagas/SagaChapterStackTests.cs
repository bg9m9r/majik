using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Sagas;

/// <summary>
/// Deferral #5 — CR 714.2b. A Saga chapter ability is a triggered ability that
/// uses the stack. When the lore counter reaches a chapter number the chapter
/// ability is placed on the stack (via the TriggerManager pending queue), so an
/// opponent receives a priority window to respond — cast an instant, activate
/// an ability — BEFORE it resolves. In particular a transforming Saga's chapter
/// III transform can be responded to.
/// </summary>
public class SagaChapterStackTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly PriorityManager _priority;
    private readonly StackResolver _resolver;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SagaChapterStackTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _priority = new PriorityManager(
            new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
        _resolver = new StackResolver(_bus, _zones = new ZoneService(_bus));
    }

    private Enchantment MakeFableWithStackRouting()
    {
        var fable = FableOfTheMirrorBreakerFactory.Create(
            _alice, _zones, _bus, _triggers);
        _zones.MoveCard(fable, ZoneType.Library, ZoneType.Battlefield, _alice);
        // Re-bind so the SagaState carries the live TriggerManager (the factory
        // already wired triggers into the chapter closures; this exercises the
        // CR 714.2b stack route).
        SagaBinder.Bind(fable, MakeEntity(fable.Name), effects: null, zones: _zones,
            triggers: _triggers, eventBus: _bus);
        return fable;
    }

    private static CardEntity MakeEntity(string name) => new()
    {
        ScryfallId = Guid.NewGuid().ToString(),
        Name = name,
        TypeLine = "Enchantment — Saga",
        OracleText =
            "I — Create a 2/2 red Goblin Shaman creature token.\n" +
            "II — You may discard up to two cards, then draw that many cards.\n" +
            "III — Exile this Saga, then return it to the battlefield transformed.",
        Colors = "R",
        ColorIdentity = "R",
        Keywords = "",
        Legalities = "",
    };

    // -----------------------------------------------------------------------
    // Chapter ability goes on the stack (CR 714.2b) — not resolved synchronously
    // -----------------------------------------------------------------------

    [Fact]
    public void ChapterTrigger_IsEnqueuedPending_NotResolvedSynchronously()
    {
        var fable = MakeFableWithStackRouting();

        // Advance to chapter I. With a TriggerManager wired, the chapter ability
        // is enqueued onto the pending queue — NOT resolved in-line.
        fable.SagaState!.AdvanceAndChapter();

        _triggers.PendingCount.Should().Be(1, "the chapter ability is a pending trigger (CR 603.3)");
        fable.SagaState.ChapterTriggerOnStack.Should()
            .BeTrue("the SBA must defer the Saga sacrifice while the chapter is unresolved");

        // The chapter effect has NOT happened yet — no Goblin token on the
        // battlefield until the trigger resolves.
        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Any(c => c.IsToken).Should().BeFalse("chapter I resolves off the stack, not in-line");
    }

    [Fact]
    public void ChapterTrigger_OnStack_OpensPriorityWindow_BeforeResolving()
    {
        var fable = MakeFableWithStackRouting();
        fable.SagaState!.AdvanceAndChapter(); // chapter I → pending

        // Drain onto the stack (Rule 603.3 — the next time a player would
        // receive priority).
        _triggers.PutPendingTriggersOnStack(_alice);

        _stack.Count.Should().Be(1, "the chapter ability is now on the stack");
        _stack.Top.Should().BeAssignableTo<ITriggeredAbility>(
            "a Saga chapter ability is a triggered ability (CR 714.2b)");

        // Still no token — the ability has not resolved; a priority window is open.
        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Any(c => c.IsToken).Should().BeFalse();

        // Resolve — now the effect lands and the SBA-defer flag clears.
        _resolver.ResolveTop(_stack);

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsToken).Should().Be(1, "the resolved chapter I creates the Goblin token");
        fable.SagaState!.ChapterTriggerOnStack.Should().BeFalse("the chapter resolved");
    }

    // -----------------------------------------------------------------------
    // An opponent can RESPOND to a Saga chapter trigger via the real priority
    // loop (CR 714.2b / 117). Chapter III transform is responded-to-able.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Opponent_GetsPriorityWindow_WithChapterTriggerOnStack()
    {
        var fable = MakeFableWithStackRouting();

        // Advance straight to the final chapter (III → transform). The chapter
        // ability is enqueued, not resolved.
        fable.SagaState!.AdvanceAndChapter(); // I
        // I was enqueued; drain + resolve it so we can advance again.
        DrainAndResolveAll();
        fable.SagaState!.AdvanceAndChapter(); // II
        DrainAndResolveAll();
        fable.SagaState!.AdvanceAndChapter(); // III → transform (enqueued)

        // At the moment Bob receives priority, the transform must NOT have
        // happened yet — Fable is still the Saga front face on the battlefield.
        // Snapshot via PriorityReceivedEvent for Bob: it fires whenever Bob is
        // handed priority, including the window AFTER the chapter ability is on
        // the stack but BEFORE it resolves.
        var stackWhenBobGetsPriority = new List<int>();
        var fableStillSagaWhenBobGetsPriority = new List<bool>();
        _bus.Subscribe<Majik.Core.Domain.DomainEvents.PriorityReceivedEvent>(e =>
        {
            if (!ReferenceEquals(e.Player, _bob)) return;
            stackWhenBobGetsPriority.Add(_stack.Count);
            fableStillSagaWhenBobGetsPriority.Add(
                fable.Zone == ZoneType.Battlefield && fable.HasSubtype(CardSubtype.Saga));
        });

        var loop = NewLoop(new DeterministicBotAgent(), new DeterministicBotAgent());
        await loop.RunUntilRoundEndsAsync(_alice);

        // Bob got priority while the chapter III ability sat on the stack (a
        // real response window), and Fable had NOT yet transformed at that point.
        stackWhenBobGetsPriority.Should().Contain(1,
            "Bob received priority with the chapter III ability on the stack — a response window (CR 714.2b)");
        // On the priority pass where the chapter III ability was still on the
        // stack, Fable had not yet transformed (it was responded-to-able).
        var idx = stackWhenBobGetsPriority.IndexOf(1);
        idx.Should().BeGreaterThanOrEqualTo(0);
        fableStillSagaWhenBobGetsPriority[idx].Should()
            .BeTrue("the chapter III transform is responded-to-able — it had not resolved when Bob got priority");

        // After the loop fully resolves the stack, the transform completed.
        _stack.IsEmpty.Should().BeTrue();
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.Name == "Reflection of Kiki-Jiki")
            .Should().BeTrue("chapter III ultimately transforms the Saga");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fable,
            "the Fable front face was exiled by the transform");
    }

    private void DrainAndResolveAll()
    {
        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty) _resolver.ResolveTop(_stack);
    }

    private PriorityLoop NewLoop(IPlayerAgent aliceAgent, IPlayerAgent bobAgent)
    {
        var agents = new Dictionary<Player, IPlayerAgent>
        {
            [_alice] = aliceAgent,
            [_bob] = bobAgent,
        };
        return new PriorityLoop(
            players: new[] { _alice, _bob },
            priority: _priority,
            stack: _stack,
            stackResolver: _resolver,
            zoneService: _zones,
            agents: agents,
            turnNumberAccessor: () => 1,
            phaseAccessor: () => PhaseStateType.PreCombatMain,
            landDropTracker: new LandDropTracker());
    }
}
