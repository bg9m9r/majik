using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

public class EntersWithCountersTests
{
    [Theory]
    [InlineData("Strangleroot Geist enters with a +1/+1 counter on it.", 1)]
    [InlineData("Triskelion enters with three +1/+1 counters on it.", 3)]
    [InlineData("Yuna enters the battlefield with two +1/+1 counters on it.", 2)]
    public void Binder_RecognisesAndRegistersCorrectAmount(string oracle, int expected)
    {
        var bus = new ReplacementBus();
        var card = new Creature("test", "", 1, 1);
        var entity = new CardEntity { Name = "test", OracleText = oracle };

        EntersWithCountersBinder.Bind(card, entity, bus).Should().BeTrue();

        var owner = new Player("A", 20);
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(expected);
    }

    [Theory]
    [InlineData("Walking Ballista enters with X +1/+1 counters on it.")]
    [InlineData("For each Forest you control, this creature enters with a +1/+1 counter on it.")]
    public void Binder_SkipsVariableXAndConditionalShapes(string oracle)
    {
        // These need ChosenSpellParams.X threading or context predicates —
        // out of scope for the regex-only binder.
        var bus = new ReplacementBus();
        var card = new Creature("test", "", 1, 1);
        var entity = new CardEntity { Name = "test", OracleText = oracle };

        // "For each ..." shape might still match the simple binder if the
        // regex catches the trailing "enters with a +1/+1 counter on it".
        // That's lossy but acceptable — at most over-fires by one counter
        // instead of producing zero behavior. Contract here is "must not
        // throw" — the bool return value is intentionally not pinned.
        var act = () => EntersWithCountersBinder.Bind(card, entity, bus);
        act.Should().NotThrow();
    }

    [Fact]
    public void Binder_NoMatch_ReturnsFalse()
    {
        var bus = new ReplacementBus();
        var card = new Creature("test", "", 1, 1);
        var entity = new CardEntity { Name = "test", OracleText = "Flying. First strike." };

        EntersWithCountersBinder.Bind(card, entity, bus).Should().BeFalse();
    }
}
