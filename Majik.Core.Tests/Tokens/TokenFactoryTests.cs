using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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

    [Fact]
    public void CreateFood_PutsArtifactTokenOnBattlefield_WithFoodSubtype()
    {
        var food = TokenFactory.CreateFood(_alice, _zones);

        food.Should().NotBeNull();
        food.IsToken.Should().BeTrue();
        food.Subtypes.Should().Contain(CardSubtype.Food);
        food.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(food);
    }

    // ── Clue activated ability ────────────────────────────────────────────────

    [Fact]
    public void CreateClue_HasSacForDrawAbility()
    {
        var alice = new Player("Alice", 20);
        var clue = TokenFactory.CreateClue(alice);

        clue.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        var ability = clue.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().HaveCount(2);   // {2} mana + sacrifice
        ability.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void CreateClue_DrawAbility_ResolvesDrawsACard()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Card("Top Card", "");
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var clue = TokenFactory.CreateClue(alice);
        var ability = clue.Abilities.OfType<ActivatedAbility>().Single();

        // Execute effects only — skip cost payment to keep the test pure.
        foreach (var e in ability.Effects) e.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topCard);
        alice.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    // ── Food activated ability ────────────────────────────────────────────────

    [Fact]
    public void CreateFood_HasTapManaSacForLifeAbility()
    {
        var alice = new Player("Alice", 20);
        var food = TokenFactory.CreateFood(alice);

        food.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        var ability = food.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().HaveCount(3);   // {2} mana + tap + sacrifice
        ability.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void CreateFood_GainLifeAbility_GainsThreeLifeOnResolution()
    {
        var alice = new Player("Alice", 20);
        var food = TokenFactory.CreateFood(alice);

        var ability = food.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        alice.LifeTotal.Should().Be(23);
    }

    // ── Blood token (Crimson Vow) ─────────────────────────────────────────────

    [Fact]
    public void CreateBlood_PutsArtifactTokenOnBattlefield_WithBloodSubtype()
    {
        var blood = TokenFactory.CreateBlood(_alice, _zones);

        blood.Should().NotBeNull();
        blood.IsToken.Should().BeTrue();
        blood.HasType(CardType.Artifact).Should().BeTrue();
        blood.Subtypes.Should().Contain(CardSubtype.Blood);
        blood.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(blood);
    }

    [Fact]
    public void CreateBlood_HasManaTapDiscardSacForDrawAbility()
    {
        var alice = new Player("Alice", 20);
        var blood = TokenFactory.CreateBlood(alice);

        blood.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        var ability = blood.Abilities.OfType<ActivatedAbility>().Single();
        // Printed costs: {1} + {T} + Discard a card + Sacrifice this artifact.
        ability.Costs.Should().HaveCount(4);
        ability.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void CreateBlood_DrawAbility_DrawsACardOnResolution()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Card("Top Card", "");
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var blood = TokenFactory.CreateBlood(alice);
        var ability = blood.Abilities.OfType<ActivatedAbility>().Single();

        // Execute effects only — skip cost payment to keep the test pure
        // (matches the Clue draw test posture).
        foreach (var e in ability.Effects) e.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topCard);
        alice.Zones.Library.GetCards().Should().NotContain(topCard);
    }
}
