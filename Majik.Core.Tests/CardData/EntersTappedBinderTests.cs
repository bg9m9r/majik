using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class EntersTappedBinderTests
{
    [Theory]
    [InlineData("Spymaster's Vault enters tapped. {T}: Add {B}.")]
    [InlineData("Underground Mortuary enters tapped. When Underground Mortuary enters, surveil 1.")]
    [InlineData("This land enters the battlefield tapped.")]
    [InlineData("Tundra enters the battlefield tapped.")]
    public void Bind_RegistersReplacement_ForUnconditionalEntersTapped(string oracleText)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Test Land");
        land.SetOwner(alice);
        var entity = new CardEntity { Name = "Test Land", OracleText = oracleText, TypeLine = "Land" };

        var bound = EntersTappedBinder.Bind(land, entity, bus);

        bound.Should().BeTrue();
    }

    [Theory]
    [InlineData("As Sacred Foundry enters, you may pay 2 life. If you don't, it enters tapped.")]
    [InlineData("Drowned Catacomb enters tapped unless you control an Island or a Swamp.")]
    [InlineData("Mountain")]
    [InlineData("")]
    public void Bind_DoesNotRegister_ForConditionalOrUnrelatedText(string oracleText)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = new Land("Test Land");
        land.SetOwner(alice);
        var entity = new CardEntity { Name = "Test Land", OracleText = oracleText, TypeLine = "Land" };

        var bound = EntersTappedBinder.Bind(land, entity, bus);

        bound.Should().BeFalse();
    }

    [Fact]
    public void Bind_BoundCardEntersTapped_WhenMovedToBattlefield()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);

        var alice = new Player("Alice", 20);
        var land = new Land("Spymaster's Vault");
        land.SetOwner(alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var entity = new CardEntity
        {
            Name = "Spymaster's Vault",
            OracleText = "Spymaster's Vault enters tapped. {T}: Add {B}.",
            TypeLine = "Land",
        };

        EntersTappedBinder.Bind(land, entity, rep).Should().BeTrue();

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        land.Zone.Should().Be(ZoneType.Battlefield);
        ((Permanent)land).IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Bind_UnboundCard_EntersUntapped()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);

        var alice = new Player("Alice", 20);
        var land = new Land("Mountain");
        land.SetOwner(alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var entity = new CardEntity { Name = "Mountain", OracleText = "({T}: Add {R}.)", TypeLine = "Basic Land — Mountain" };

        EntersTappedBinder.Bind(land, entity, rep).Should().BeFalse();

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeFalse();
    }
}
