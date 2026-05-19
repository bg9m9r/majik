using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Sagas;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

public class SagaProgressionTests
{
    private readonly Player _alice = new("Alice", 20);

    private Enchantment MakeSaga()
    {
        var saga = new Enchantment("History of Benalia", "2W",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Saga })
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(saga);
        return saga;
    }

    [Fact]
    public void AdvanceAndChapter_FiresChapterCallback()
    {
        var saga = MakeSaga();
        var firedChapters = new List<int>();
        var state = new SagaState(saga, finalChapter: 3,
            onChapter: ch => firedChapters.Add(ch));
        saga.SagaState = state;

        state.AdvanceAndChapter();
        firedChapters.Should().Equal(1);
        state.AdvanceAndChapter();
        firedChapters.Should().Equal(1, 2);
    }

    [Fact]
    public void ShouldBeSacrificed_FalseWhileChapterTriggerOnStack()
    {
        var saga = MakeSaga();
        var state = new SagaState(saga, finalChapter: 3);
        saga.SagaState = state;

        // Advance to final chapter
        state.AdvanceAndChapter(); // ch 1
        state.AdvanceAndChapter(); // ch 2
        state.AdvanceAndChapter(); // ch 3

        // Engine signals the chapter trigger is on the stack; SBA defers.
        state.ChapterTriggerOnStack = true;
        state.ShouldBeSacrificed().Should().BeFalse();

        // Once the trigger resolves and stack flag cleared, SBA acts.
        state.ChapterTriggerOnStack = false;
        state.ShouldBeSacrificed().Should().BeTrue();
    }

    [Fact]
    public void StateBasedActions_SacrificesCompletedSaga()
    {
        var saga = MakeSaga();
        var state = new SagaState(saga, finalChapter: 2);
        saga.SagaState = state;
        state.AdvanceAndChapter();
        state.AdvanceAndChapter();

        var sba = new StateBasedActions();
        sba.CheckStateBasedActions(new[] { _alice },
            _alice.Zones.Battlefield.GetCards().ToList());

        saga.Zone.Should().Be(ZoneType.Graveyard);
    }
}
