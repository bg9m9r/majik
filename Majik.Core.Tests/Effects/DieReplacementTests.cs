using FluentAssertions;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class DieReplacementTests
{
    [Fact]
    public void IfCreatureWouldDie_ExileInstead_LandsInExileNotGraveyard()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);
        var sba = new StateBasedActions(eventBus, zones);

        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Controller = alice };
        bear.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(bear);
        bear.TakeDamage(5);

        // Register: "if Bear would be put into a graveyard from the battlefield, exile it instead"
        rep.Register<ZoneMoveIntent>(new LambdaReplacement<ZoneMoveIntent>(
            (i, _) => ReferenceEquals(i.Card, bear)
                      && i.FromZone == ZoneType.Battlefield
                      && i.ToZone == ZoneType.Graveyard,
            (i, _) => i with { ToZone = ZoneType.Exile }));

        sba.CheckStateBasedActions(new[] { alice }, new[] { (Majik.Core.Cards.ICard)bear });

        bear.Zone.Should().Be(ZoneType.Exile);
        alice.Zones.Exile.GetCards().Should().Contain(bear);
        alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }
}
