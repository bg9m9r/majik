using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Engine-level tests for <see cref="ExploreAction"/> (CR 701.40).
///
/// Covers the full keyword-action sequence:
/// - Land on top → into hand, no counter (CR 701.40b).
/// - Non-land on top → +1/+1 counter on the exploring permanent, then keep
///   on top (CR 701.40c, agent keeps) or graveyard (agent declines).
/// - Empty library → +1/+1 counter, nothing moves (CR 701.40d).
/// - <see cref="CreatureExploredEvent"/> is published after resolution with
///   the correct controller / revealed card / land flag (CR 701.40e).
/// </summary>
public class ExploreActionTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly TestEventBus _bus = new();

    public void Dispose()
    {
        AgentRegistry.Clear();
        EventBusRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    private static Creature Explorer() => new("Explorer", "{G}", 1, 1);

    private async Task ExploreAsync(Creature explorer, IPlayerAgent? agent = null)
    {
        await ExploreAction.ExploreAsync(
            creature: explorer,
            controller: _alice,
            agent: agent,
            game: null,
            replacements: null,
            eventBus: _bus,
            zones: null,
            ct: default);
    }

    // -----------------------------------------------------------------------
    // CR 701.40b — land on top goes to hand, no counter.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Explore_LandOnTop_GoesToHand_NoCounter()
    {
        var land = new Land("Forest");
        var below = new Creature("Below", "{G}", 1, 1);
        _alice.Zones.Library.AddCard(land);  // top
        _alice.Zones.Library.AddCard(below); // second

        var explorer = Explorer();
        await ExploreAsync(explorer);

        _alice.Zones.Hand.GetCards().Should().Contain(land,
            "CR 701.40b — a revealed land goes into the controller's hand");
        _alice.Zones.Library.GetCards().Should().NotContain(land);
        _alice.Zones.Library.GetCards().Should().Contain(below);
        explorer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 701.40b — a revealed land places NO +1/+1 counter");
    }

    // -----------------------------------------------------------------------
    // CR 701.40c — non-land on top: counter + keep on top.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Explore_NonLandOnTop_KeepOnTop_PlacesCounter_LeavesCardOnTop()
    {
        var spell = new Creature("Spell", "{G}", 2, 2); // non-land
        var below = new Creature("Below", "{G}", 1, 1);
        _alice.Zones.Library.AddCard(spell);  // top
        _alice.Zones.Library.AddCard(below);  // second

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true); // keep on top

        var explorer = Explorer();
        await ExploreAsync(explorer, agent);

        explorer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40c — a non-land reveal puts a +1/+1 counter on the explorer");
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(spell,
            "agent chose to keep the revealed card on top of the library");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // CR 701.40c — non-land on top: counter + graveyard.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Explore_NonLandOnTop_Graveyard_PlacesCounter_BinsCard()
    {
        var spell = new Creature("Spell", "{G}", 2, 2); // non-land
        var below = new Creature("Below", "{G}", 1, 1);
        _alice.Zones.Library.AddCard(spell);  // top
        _alice.Zones.Library.AddCard(below);  // second

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(false); // graveyard

        var explorer = Explorer();
        await ExploreAsync(explorer, agent);

        explorer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40c — a non-land reveal puts a +1/+1 counter on the explorer");
        _alice.Zones.Graveyard.GetCards().Should().Contain(spell,
            "agent chose to put the revealed card into the graveyard");
        _alice.Zones.Library.GetCards().Should().NotContain(spell);
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(below,
            "the next card is now on top");
    }

    // -----------------------------------------------------------------------
    // Default (no agent registered) keeps the card on top.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Explore_NonLandOnTop_NoAgent_KeepsOnTopByDefault()
    {
        var spell = new Creature("Spell", "{G}", 2, 2);
        _alice.Zones.Library.AddCard(spell);

        var explorer = Explorer();
        await ExploreAsync(explorer, agent: null);

        explorer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(spell,
            "with no agent the library-preserving default keeps the card on top");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // CR 701.40d — empty library: counter only, no card moves.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Explore_EmptyLibrary_PlacesCounter_NoCardMoves()
    {
        // Library intentionally empty.
        var explorer = Explorer();
        await ExploreAsync(explorer);

        explorer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40d — an empty library still places the +1/+1 counter");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // CR 701.40e — the explore event fires with correct payload.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Explore_NonLand_PublishesEvent_WithControllerAndCard()
    {
        var spell = new Creature("Spell", "{G}", 2, 2);
        _alice.Zones.Library.AddCard(spell);

        CreatureExploredEvent? captured = null;
        _bus.Subscribe<CreatureExploredEvent>(e => captured = e);

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);

        var explorer = Explorer();
        await ExploreAsync(explorer, agent);

        captured.Should().NotBeNull("CR 701.40e — an explore publishes a CreatureExploredEvent");
        captured!.Controller.Should().BeSameAs(_alice);
        captured.Creature.Should().BeSameAs(explorer);
        captured.RevealedCard.Should().BeSameAs(spell);
        captured.RevealedLand.Should().BeFalse();
    }

    [Fact]
    public async Task Explore_Land_PublishesEvent_WithLandFlag()
    {
        var land = new Land("Forest");
        _alice.Zones.Library.AddCard(land);

        CreatureExploredEvent? captured = null;
        _bus.Subscribe<CreatureExploredEvent>(e => captured = e);

        var explorer = Explorer();
        await ExploreAsync(explorer);

        captured.Should().NotBeNull();
        captured!.RevealedLand.Should().BeTrue("CR 701.40b — the revealed card was a land");
        captured.RevealedCard.Should().BeSameAs(land);
    }

    [Fact]
    public async Task Explore_EmptyLibrary_PublishesEvent_WithNullCard()
    {
        CreatureExploredEvent? captured = null;
        _bus.Subscribe<CreatureExploredEvent>(e => captured = e);

        var explorer = Explorer();
        await ExploreAsync(explorer);

        captured.Should().NotBeNull();
        captured!.RevealedCard.Should().BeNull("CR 701.40d — the library was empty");
        captured.RevealedLand.Should().BeFalse();
    }
}
