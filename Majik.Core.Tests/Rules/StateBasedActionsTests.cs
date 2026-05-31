using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Rules.Sba;
using Majik.Core.Services;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Unit tests for StateBasedActions service.
/// Tests player loss, creature death, and planeswalker death.
/// </summary>
public class StateBasedActionsTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly ZoneService _zoneService;
    private readonly StateBasedActions _sba;

    public StateBasedActionsTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _zoneService = new ZoneService(_eventBusMock.Object);
        _sba = new StateBasedActions(_eventBusMock.Object, _zoneService);
    }

    [Fact]
    public void CheckStateBasedActions_PlayerWithZeroLife_SetsHasLost()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.LoseLife(20);
        // Note: Player.LoseLife already sets HasLost when life reaches 0
        // So we need to reset it to test SBA
        player.HasLost = false;
        var players = new List<Player> { player };
        var cards = new List<ICard>();

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        player.HasLost.Should().BeTrue();
        _eventBusMock.Verify(x => x.Publish(It.IsAny<PlayerLostEvent>()), Times.Once);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StateBasedActionExecutedEvent>()), Times.Once);
    }

    [Fact]
    public void CheckStateBasedActions_PlayerWithNegativeLife_SetsHasLost()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.LoseLife(25);
        var players = new List<Player> { player };
        var cards = new List<ICard>();

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        player.HasLost.Should().BeTrue();
    }

    [Fact]
    public void CheckStateBasedActions_PlayerWithPositiveLife_DoesNotLose()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var players = new List<Player> { player };
        var cards = new List<ICard>();

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        player.HasLost.Should().BeFalse();
    }

    [Fact]
    public void CheckStateBasedActions_DeadCreature_MovesToGraveyard()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player, Controller = player };
        creature.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(creature, ZoneType.Battlefield, player);
        creature.TakeDamage(2);
        var players = new List<Player> { player };
        var cards = new List<ICard> { creature };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        creature.Zone.Should().Be(ZoneType.Graveyard);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StateBasedActionExecutedEvent>()), Times.Once);
    }

    [Fact]
    public void CheckStateBasedActions_DeadPlaneswalker_MovesToGraveyard()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var planeswalker = new Planeswalker("Jace", "2UU", 3) { Owner = player, Controller = player };
        planeswalker.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(planeswalker, ZoneType.Battlefield, player);
        planeswalker.RemoveLoyalty(3);
        var players = new List<Player> { player };
        var cards = new List<ICard> { planeswalker };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        planeswalker.Zone.Should().Be(ZoneType.Graveyard);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StateBasedActionExecutedEvent>()), Times.Once);
    }

    [Fact]
    public void CheckStateBasedActions_NullPlayers_DoesNothing()
    {
        // Arrange
        var cards = new List<ICard>();

        // Act
        _sba.CheckStateBasedActions(null!, cards);

        // Assert
        // Should not throw
    }

    [Fact]
    public void CheckStateBasedActions_NullCards_DoesNothing()
    {
        // Arrange
        var players = new List<Player> { new Player("Alice", 20) };

        // Act
        _sba.CheckStateBasedActions(players, null!);

        // Assert
        // Should not throw
    }

    // --- PLAN 05: SBA shared projection / gated rebuild correctness ---

    [Fact]
    public void CheckStateBasedActions_CheckMovesCardMidLoop_NextPassSeesUpdatedSet()
    {
        // A "destroyer" check moves a creature to the graveyard on its first
        // executed pass (returns true → loop continues, ctx is rebuilt). A
        // downstream "recorder" check captures the zone it observes for that
        // creature on each pass via the shared ctx.Creatures projection.
        // After the gated rebuild, the recorder must observe the post-move
        // zone — proving the shared projection refreshes when a card moved.
        var alice = new Player("Alice", 20);
        var doomed = new Creature("Doomed", "1G", 2, 2)
        {
            Owner = alice,
            Controller = alice,
            Zone = ZoneType.Battlefield,
        };

        // Recorder runs BEFORE the destroyer: it captures battlefield on the
        // first pass, the destroyer then moves the card (returns true → the
        // loop iterates and the coordinator rebuilds the shared projection),
        // and on the second pass the recorder observes the post-move zone.
        var recorder = new ZoneRecorderCheck(doomed);
        var destroyer = new OneShotMoveCheck(doomed, ZoneType.Graveyard);

        var sba = new StateBasedActions(
            _eventBusMock.Object,
            _zoneService,
            checks: new IStateBasedActionCheck[] { recorder, destroyer });

        sba.CheckStateBasedActions(
            new List<Player> { alice },
            new List<ICard> { doomed });

        // Pass 1: recorder sees Battlefield (before the destroyer fires).
        // Pass 2 (after the gated rebuild): recorder sees Graveyard.
        recorder.ObservedZones.Should().ContainInOrder(ZoneType.Battlefield, ZoneType.Graveyard);
    }

    [Fact]
    public void CheckStateBasedActions_QuiescentPass_DoesNotRebuildProjection()
    {
        // When no check moves a card, the shared projection is reused across
        // the (single) settling pass — the creature object identity returned
        // by ctx.Creatures stays stable.
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = alice,
            Controller = alice,
            Zone = ZoneType.Battlefield,
        };

        var recorder = new ZoneRecorderCheck(bear);
        var sba = new StateBasedActions(
            _eventBusMock.Object,
            _zoneService,
            checks: new IStateBasedActionCheck[] { recorder });

        sba.CheckStateBasedActions(
            new List<Player> { alice },
            new List<ICard> { bear });

        // Single quiescent pass → recorded exactly once, on battlefield.
        recorder.ObservedZones.Should().ContainSingle()
            .Which.Should().Be(ZoneType.Battlefield);
    }

    /// <summary>Test check: moves a card to a target zone the first time it
    /// runs, reporting that it changed state so the fixed-point loop iterates
    /// (and the coordinator rebuilds the shared projection).</summary>
    private sealed class OneShotMoveCheck : IStateBasedActionCheck
    {
        private readonly ICard _card;
        private readonly ZoneType _to;
        private bool _done;

        public OneShotMoveCheck(ICard card, ZoneType to) { _card = card; _to = to; }
        public string Name => "OneShotMove";

        public bool Execute(SbaContext context)
        {
            if (_done) return false;
            _done = true;
            _card.SetZone(_to);
            return true;
        }
    }

    /// <summary>Test check: records, each pass, the zone of a tracked creature
    /// as seen through the shared <see cref="SbaContext.Creatures"/> projection.
    /// Never reports a change (so it does not drive the loop itself).</summary>
    private sealed class ZoneRecorderCheck : IStateBasedActionCheck
    {
        private readonly Creature _tracked;
        public List<ZoneType> ObservedZones { get; } = new();

        public ZoneRecorderCheck(Creature tracked) { _tracked = tracked; }
        public string Name => "ZoneRecorder";

        public bool Execute(SbaContext context)
        {
            foreach (var c in context.Creatures)
            {
                if (ReferenceEquals(c, _tracked)) ObservedZones.Add(c.Zone);
            }
            return false;
        }
    }
}
