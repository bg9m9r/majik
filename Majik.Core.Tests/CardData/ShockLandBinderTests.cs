using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

public class ShockLandBinderTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ShockLand_HighLife_EntersUntapped_Pays2Life()
    {
        var bus = new ReplacementBus();
        var sacredFoundry = new Land("Sacred Foundry") { Owner = _alice, Zone = ZoneType.Hand };
        ShockLandBinder.Bind(sacredFoundry,
            new CardEntity { Name = "Sacred Foundry",
              OracleText = "({T}: Add {R} or {W}.)\nAs this land enters, you may pay 2 life. If you don't, it enters tapped." },
            bus);

        var zones = new Majik.Core.Services.ZoneService(replacements: bus);
        zones.MoveCard(sacredFoundry, ZoneType.Hand, ZoneType.Battlefield, controller: _alice);

        sacredFoundry.Zone.Should().Be(ZoneType.Battlefield);
        ((Permanent)sacredFoundry).IsTapped.Should().BeFalse();
        _alice.LifeTotal.Should().Be(18);
    }

    [Fact]
    public void ShockLand_LowLife_EntersTapped_NoLifePaid()
    {
        var bus = new ReplacementBus();
        var steamVents = new Land("Steam Vents") { Owner = _alice, Zone = ZoneType.Hand };
        ShockLandBinder.Bind(steamVents,
            new CardEntity { Name = "Steam Vents",
              OracleText = "({T}: Add {U} or {R}.)\nAs this land enters, you may pay 2 life. If you don't, it enters tapped." },
            bus);

        _alice.LifeTotal = 2;
        var zones = new Majik.Core.Services.ZoneService(replacements: bus);
        zones.MoveCard(steamVents, ZoneType.Hand, ZoneType.Battlefield, controller: _alice);

        ((Permanent)steamVents).IsTapped.Should().BeTrue();
        _alice.LifeTotal.Should().Be(2); // no life paid
    }

    [Fact]
    public void NonShockLand_NotBound()
    {
        var bus = new ReplacementBus();
        var mountain = new Land("Mountain") { Owner = _alice };
        var bound = ShockLandBinder.Bind(mountain,
            new CardEntity { Name = "Mountain", OracleText = "({T}: Add {R}.)" },
            bus);
        bound.Should().BeFalse();
    }
}
