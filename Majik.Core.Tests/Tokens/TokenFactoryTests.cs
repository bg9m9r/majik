using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class TokenFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);

    public TokenFactoryTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public void Create_PutsTokenOnBattlefield_FlaggedAsToken()
    {
        var token = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Spirit", 1, 1, Keywords: new[] { "Flying" }),
            _alice, _zones);

        token.Zone.Should().Be(ZoneType.Battlefield);
        token.IsToken.Should().BeTrue();
        token.Power.Should().Be(1);
        Majik.Core.Combat.CombatAbilities.HasFlying(token).Should().BeTrue();
    }

    [Fact]
    public void Token_MovedToGraveyard_CeasesToExistViaSBA()
    {
        var token = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1), _alice, _zones);

        // Move to graveyard (simulating death).
        _zones.MoveCardTo(token, ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(token);

        // SBA removes it.
        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { token });

        _alice.Zones.Graveyard.GetCards().Should().NotContain(token);
        _alice.Zones.Exile.GetCards().Should().NotContain(token);
    }

    [Fact]
    public void NonToken_NotAffectedBySBAToken()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { bear });

        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }
}
