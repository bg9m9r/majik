using FluentAssertions;
using Majik.Core.CardData.Sagas;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

public class SagaStateTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NewSaga_ZeroLoreCounters_NotSacrificed()
    {
        var saga = new Enchantment("History of Benalia", "1WW") { Owner = _alice, Controller = _alice };
        var state = new SagaState(saga, finalChapter: 3);

        state.LoreCounters.Should().Be(0);
        state.ShouldBeSacrificed().Should().BeFalse();
    }

    [Fact]
    public void Advance_AddsLoreCounter_ReturnsNewCount()
    {
        var saga = new Enchantment("History of Benalia", "1WW") { Owner = _alice, Controller = _alice };
        var state = new SagaState(saga, finalChapter: 3);

        state.AdvanceAndChapter().Should().Be(1);
        state.AdvanceAndChapter().Should().Be(2);
        state.AdvanceAndChapter().Should().Be(3);
    }

    [Fact]
    public void Advance_PastFinalChapter_FlagsForSacrifice()
    {
        var saga = new Enchantment("History of Benalia", "1WW") { Owner = _alice, Controller = _alice };
        var state = new SagaState(saga, finalChapter: 3);

        state.AdvanceAndChapter();
        state.AdvanceAndChapter();
        state.AdvanceAndChapter();

        state.ShouldBeSacrificed().Should().BeTrue();
    }

    [Fact]
    public void Saga_BeforeFinal_NotSacrificed()
    {
        var saga = new Enchantment("Phyrexian Scriptures", "2B") { Owner = _alice, Controller = _alice };
        var state = new SagaState(saga, finalChapter: 3);

        state.AdvanceAndChapter();
        state.AdvanceAndChapter();

        state.ShouldBeSacrificed().Should().BeFalse();
    }
}
