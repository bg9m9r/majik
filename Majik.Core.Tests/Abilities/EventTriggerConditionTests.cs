using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Abilities;

public class EventTriggerConditionTests
{
    private readonly Player _player = new("Alice", 20);
    private readonly Mock<ITriggeredAbility> _abilityMock = new();

    [Fact]
    public void EventType_ReturnsConstructedType()
    {
        var condition = new EventTriggerCondition<CardMovedEvent>((_, _) => true);

        condition.EventType.Should().Be(typeof(CardMovedEvent));
    }

    [Fact]
    public void Matches_ReturnsFalse_ForUnrelatedEventType()
    {
        var condition = new EventTriggerCondition<CardMovedEvent>((_, _) => true);
        var card = new Instant("Lightning Bolt", "R") { Owner = _player };
        var unrelated = new CardDrawnEvent(card, _player);

        condition.Matches(unrelated, _abilityMock.Object).Should().BeFalse();
    }

    [Fact]
    public void Matches_InvokesPredicate_ForCorrectEventType()
    {
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => e.ToZone == ZoneType.Battlefield);
        var card = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _player };
        var matching = new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield);
        var nonMatching = new CardMovedEvent(card, ZoneType.Hand, ZoneType.Graveyard);

        condition.Matches(matching, _abilityMock.Object).Should().BeTrue();
        condition.Matches(nonMatching, _abilityMock.Object).Should().BeFalse();
    }

    [Fact]
    public void Matches_PassesAbilityToPredicate()
    {
        ITriggeredAbility? captured = null;
        var condition = new EventTriggerCondition<CardDrawnEvent>((_, a) =>
        {
            captured = a;
            return true;
        });
        var card = new Instant("Test", "1") { Owner = _player };
        var evt = new CardDrawnEvent(card, _player);

        condition.Matches(evt, _abilityMock.Object);

        captured.Should().BeSameAs(_abilityMock.Object);
    }

    [Fact]
    public void Constructor_NullPredicate_Throws()
    {
        var act = () => new EventTriggerCondition<CardMovedEvent>(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
