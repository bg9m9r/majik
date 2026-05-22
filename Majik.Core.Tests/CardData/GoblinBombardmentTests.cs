using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinBombardmentFactory"/>.
///
/// Covers:
/// - Card identity (Enchantment, mana cost {1}{R}).
/// - Single activated ability with a sacrifice cost.
/// - Damage to chosen target (Creature, Player).
/// - Cost cannot be paid without a creature to sacrifice.
/// </summary>
public class GoblinBombardmentTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinBombardment_IsEnchantment()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        goblin.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void GoblinBombardment_NameIsCorrect()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        goblin.Name.Should().Be("Goblin Bombardment");
    }

    [Fact]
    public void GoblinBombardment_OwnerAndControllerAreSet()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        goblin.Owner.Should().BeSameAs(_alice);
        goblin.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinBombardment_HasExactlyOneActivatedAbility()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        goblin.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void GoblinBombardment_Ability_HasSacrificeAnotherCreatureCost()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        var ability = goblin.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<SacrificeAnotherCreatureCost>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Cost cannot be paid without an eligible creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_CannotPay_WhenControllerHasNoCreatures()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var ability = goblin.Abilities.OfType<ActivatedAbility>().Single();
        var sac = ability.Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        sac.CanPay(_alice).Should().BeFalse(
            "no creature on the battlefield to sacrifice");
    }

    [Fact]
    public void Cost_CanPay_WhenControllerHasCreature()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice); bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var ability = goblin.Abilities.OfType<ActivatedAbility>().Single();
        var sac = ability.Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        sac.CanPay(_alice).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // End-to-end activation: cost pays, effect deals 1 damage to a target
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_DamagesChosenPlayerTarget()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice); bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var ability = (GoblinBombardmentAbility)goblin.Abilities.OfType<ActivatedAbility>().Single();
        ability.SacrificeChoice.Target = bear;
        ability.DamageTarget = _bob;

        // Pay the cost (sacrifices the bear); then resolve.
        foreach (var c in ability.Costs) c.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard, "sacrificed");
        _bob.LifeTotal.Should().Be(19, "took 1 damage");
    }

    [Fact]
    public void Activation_DamagesChosenCreatureTarget()
    {
        var goblin = GoblinBombardmentFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var sacFodder = new Creature("Goblin Token", "R", 1, 1);
        sacFodder.SetOwner(_alice); sacFodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sacFodder);
        sacFodder.SetZone(ZoneType.Battlefield);

        var enemy = new Creature("Grizzly Bears", "1G", 2, 2);
        enemy.SetOwner(_bob); enemy.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);

        var ability = (GoblinBombardmentAbility)goblin.Abilities.OfType<ActivatedAbility>().Single();
        ability.SacrificeChoice.Target = sacFodder;
        ability.DamageTarget = enemy;

        foreach (var c in ability.Costs) c.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        sacFodder.Zone.Should().Be(ZoneType.Graveyard);
        enemy.Damage.Should().Be(1, "took 1 damage from the ping");
    }

    // -----------------------------------------------------------------------
    // Bot wiring: pick-first-legal heuristic
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateForBot_PicksFirstControllerCreatureAndFirstOpponentTarget()
    {
        var sacFodder = new Creature("Token", "R", 1, 1);
        sacFodder.SetOwner(_alice); sacFodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sacFodder);
        sacFodder.SetZone(ZoneType.Battlefield);

        var enemy = new Creature("Bear", "1G", 2, 2);
        enemy.SetOwner(_bob); enemy.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);

        var goblin = GoblinBombardmentFactory.CreateForBot(_alice, new[] { _alice, _bob });
        var ability = (GoblinBombardmentAbility)goblin.Abilities.OfType<ActivatedAbility>().Single();

        ability.SacrificeChoice.Target.Should().BeSameAs(sacFodder,
            "first creature the controller controls");
        ability.DamageTarget.Should().BeSameAs(enemy,
            "first creature an opponent controls");
    }

    [Fact]
    public void CreateForBot_FallsBackToOpponentPlayer_WhenNoEnemyCreatures()
    {
        var sacFodder = new Creature("Token", "R", 1, 1);
        sacFodder.SetOwner(_alice); sacFodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sacFodder);
        sacFodder.SetZone(ZoneType.Battlefield);

        var goblin = GoblinBombardmentFactory.CreateForBot(_alice, new[] { _alice, _bob });
        var ability = (GoblinBombardmentAbility)goblin.Abilities.OfType<ActivatedAbility>().Single();

        ability.DamageTarget.Should().BeSameAs(_bob,
            "no opponent creatures — damage routes to the opponent player");
    }
}
