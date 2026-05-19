using FluentAssertions;
using Majik.Core.CardData.Battles;
using Majik.Core.CardData.Sagas;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

public class BattleSagaSBATests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);

    public BattleSagaSBATests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public void Battle_AtZeroDefense_SBAMovesToGraveyard()
    {
        var b = new Enchantment("Siege", "3R")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(b);
        b.BattleState = new BattleState(b, initialDefense: 0);

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { b });

        b.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Battle_PositiveDefense_Stays()
    {
        var b = new Enchantment("Siege", "3R")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(b);
        b.BattleState = new BattleState(b, initialDefense: 3);

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { b });

        b.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Saga_FinalChapterReached_SBASacrifices()
    {
        var s = new Enchantment("History of Benalia", "1WW")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(s);
        s.SagaState = new SagaState(s, finalChapter: 3);
        s.SagaState.AdvanceAndChapter();
        s.SagaState.AdvanceAndChapter();
        s.SagaState.AdvanceAndChapter();

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { s });

        s.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Saga_MidStory_Stays()
    {
        var s = new Enchantment("Saga", "1B")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(s);
        s.SagaState = new SagaState(s, finalChapter: 3);
        s.SagaState.AdvanceAndChapter();

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { s });

        s.Zone.Should().Be(ZoneType.Battlefield);
    }
}
