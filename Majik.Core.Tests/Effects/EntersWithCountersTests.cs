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
    [InlineData("This creature enters with X +1/+1 counters on it.")]
    public void Binder_VariableX_ReadsPendingCastX(string oracle)
    {
        // CR 614.1d + CR 202.3b — "enters with X +1/+1 counters" reads the X
        // chosen at cast time (stamped on the card as PendingCastX by
        // SpellCastFlow). The binder must register a dynamic replacement so the
        // permanent enters WITH the counters (no transient 0/0 window).
        var bus = new ReplacementBus();
        var card = new Creature("test", "", 0, 0);
        var entity = new CardEntity { Name = "test", OracleText = oracle };

        EntersWithCountersBinder.Bind(card, entity, bus).Should().BeTrue();

        var owner = new Player("A", 20);
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);

        // Cast-time X = 3 (Walking Ballista cast for {3}{3}).
        card.SetPendingCastX(3);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "the permanent enters with X (=3) +1/+1 counters");
    }

    [Fact]
    public void Binder_VariableX_ZeroX_NoCounters()
    {
        // X = 0 (or never stamped) → zero counters → a 0/0 that the SBA layer
        // sends to the graveyard. Confirms no spurious counters.
        var bus = new ReplacementBus();
        var card = new Creature("test", "", 0, 0);
        var entity = new CardEntity { Name = "test", OracleText = "This creature enters with X +1/+1 counters on it." };

        EntersWithCountersBinder.Bind(card, entity, bus).Should().BeTrue();

        var owner = new Player("A", 20);
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        // No SetPendingCastX → X defaults to 0.

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void Binder_Conditional_ForEach_DoesNotThrow()
    {
        // "For each Forest you control ..." needs a context predicate — still
        // out of scope. Contract: must not throw.
        var bus = new ReplacementBus();
        var card = new Creature("test", "", 1, 1);
        var entity = new CardEntity
        {
            Name = "test",
            OracleText = "For each Forest you control, this creature enters with a +1/+1 counter on it.",
        };

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
