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

    // ── Blood token (CR 111.10 — Innistrad: Crimson Vow) ─────────────────────

    [Fact]
    public void CreateBlood_PutsRedArtifactTokenOnBattlefield_WithBloodSubtype()
    {
        var blood = TokenFactory.CreateBlood(_alice, _zones);

        blood.Should().NotBeNull();
        blood.IsToken.Should().BeTrue();
        blood.Subtypes.Should().Contain(CardSubtype.Blood);
        blood.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(blood);
        // CR 111.10 — Blood tokens are red artifacts.
        Majik.Core.Cards.CardColors.GetColors(blood)
            .Should().Contain(Majik.Core.ValueObjects.ManaColor.Red);
    }

    [Fact]
    public void CreateBlood_HasManaTapDiscardSacForDrawAbility()
    {
        var alice = new Player("Alice", 20);
        var blood = TokenFactory.CreateBlood(alice);

        var abilities = blood.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1);
        var ability = abilities[0];
        // {1} mana + tap + discard a card + sacrifice this artifact = 4 costs.
        ability.Costs.Should().HaveCount(4);
        ability.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void CreateBlood_DrawAbility_ResolvesDrawsACard_AndSacrificesSelf()
    {
        var alice = new Player("Alice", 20);
        var blood = TokenFactory.CreateBlood(alice);

        // Library top.
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var ability = blood.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Draw happened.
        alice.Zones.Hand.GetCards().Should().Contain(top);
        // Self-sacrifice happened (battlefield → graveyard).
        blood.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Battlefield.GetCards().Should().NotContain(blood);
        alice.Zones.Graveyard.GetCards().Should().Contain(blood);
    }

    // ── Map token (CR 111.10 / CR 701.40 — The Lost Caverns of Ixalan) ───────

    [Fact]
    public void CreateMap_PutsColorlessArtifactTokenOnBattlefield_WithMapSubtype()
    {
        var map = TokenFactory.CreateMap(_alice, _zones);

        map.Should().NotBeNull();
        map.IsToken.Should().BeTrue();
        map.HasType(CardType.Artifact).Should().BeTrue();
        map.Subtypes.Should().Contain(CardSubtype.Map);
        map.Zone.Should().Be(ZoneType.Battlefield);
        Majik.Core.Cards.CardColors.GetColors(map).Should().BeEmpty(
            "CR 111.10 — Map tokens are colourless artifacts");
    }

    [Fact]
    public void CreateMap_HasSorcerySpeedExploreAbility_ManaTapSac_OneTarget()
    {
        var map = TokenFactory.CreateMap(_alice);

        var ability = map.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().HaveCount(3, "{1} mana + tap + sacrifice");
        ability.IsSorcerySpeed.Should().BeTrue(
            "'Activate only as a sorcery' (CR 117.1a / 307.5)");
        ability.TargetRequests.Should().ContainSingle();
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("creature you control");
    }

    [Fact]
    public void CreateMap_ExploreAbility_TargetExplores_AndMapIsSacrificed()
    {
        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);
        Majik.Core.Players.Agents.AgentRegistry.Set(_alice, agent);
        Majik.Core.Services.ZoneServiceRegistry.Set(_alice, _zones);
        Majik.Core.Events.EventBusRegistry.Set(_alice, _bus);
        try
        {
            // Non-land on top → +1/+1 counter on the explorer (CR 701.40c).
            var spell = new Creature("Big", "{G}", 3, 3);
            _alice.Zones.Library.AddCard(spell);
            spell.SetZone(ZoneType.Library);

            var target = new Creature("Scout", "{G}", 1, 1) { Owner = _alice, Controller = _alice };
            _zones.MoveCardTo(target, ZoneType.Battlefield, _alice);

            var map = TokenFactory.CreateMap(_alice, _zones);
            var ability = map.Abilities.OfType<ActivatedAbility>().Single();
            ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

            ability.Resolve();

            target.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne).Should()
                .Be(1, "CR 701.40c — the +1/+1 counter lands on the exploring target");
            map.Zone.Should().Be(ZoneType.Graveyard, "Sacrifice this token");
            _alice.Zones.Battlefield.GetCards().Should().NotContain(map);
        }
        finally
        {
            Majik.Core.Players.Agents.AgentRegistry.Clear();
            Majik.Core.Services.ZoneServiceRegistry.Clear();
            Majik.Core.Events.EventBusRegistry.Clear();
        }
    }

    [Fact]
    public void CreateMap_ExploreAbility_LandOnTop_GoesToHand_AndMapSacrificed()
    {
        Majik.Core.Services.ZoneServiceRegistry.Set(_alice, _zones);
        try
        {
            var land = new Land("Forest") { Owner = _alice };
            _alice.Zones.Library.AddCard(land);
            land.SetZone(ZoneType.Library);

            var target = new Creature("Scout", "{G}", 1, 1) { Owner = _alice, Controller = _alice };
            _zones.MoveCardTo(target, ZoneType.Battlefield, _alice);

            var map = TokenFactory.CreateMap(_alice, _zones);
            var ability = map.Abilities.OfType<ActivatedAbility>().Single();
            ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

            ability.Resolve();

            _alice.Zones.Hand.GetCards().Should().Contain(land,
                "CR 701.40b — a revealed land goes to the controller's hand");
            target.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne).Should().Be(0);
            map.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            Majik.Core.Services.ZoneServiceRegistry.Clear();
        }
    }
}
