using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Adanto Vanguard (Ixalan, {1}{W}).
///
/// Oracle (verified against Scryfall):
///   "As long as this creature is attacking, it gets +2/+0.
///    Pay 4 life: This creature gains indestructible until end of turn."
///
/// Covers:
///   - Card shape: name, type, Vampire + Soldier subtypes, P/T 1/1, {1}{W}.
///   - "While attacking" static +2/+0 (3/1 attacking, 1/1 otherwise).
///   - Pay-4-life activated ability grants Indestructible EOT.
///   - PayLifeCost gates on life total (CR 119.4).
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "W")]
public class AdantoVanguardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void AdantoVanguard_IsCreature_VampireSoldier_1_1_AtCost1W()
    {
        var c = AdantoVanguardFactory.Create(_alice);

        c.Name.Should().Be("Adanto Vanguard");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AdantoVanguard_HasOnePayLifeActivatedAbility()
    {
        var c = AdantoVanguardFactory.Create(_alice);

        var abilities = c.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1);
        abilities[0].Costs.OfType<PayLifeCost>().Single().Amount.Should().Be(4);
    }

    [Fact]
    public void AdantoVanguard_NotAttacking_IsBase1_1()
    {
        var svc = new ContinuousEffectsService();
        var combat = new CombatManager(); // no current combat
        var c = AdantoVanguardFactory.Create(_alice, svc, combat);

        var chars = svc.Compute(c);
        chars.Power.Should().Be(1);
        chars.Toughness.Should().Be(1);
    }

    [Fact]
    public void AdantoVanguard_WhileAttacking_Gets3_1()
    {
        var svc = new ContinuousEffectsService();
        var combat = new CombatManager();
        var c = AdantoVanguardFactory.Create(_alice, svc, combat);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.ClearSummoningSickness(); // CR 302.6 — eligible to attack.

        // Declare the Vanguard as an attacker against Bob.
        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(c, targetPlayer: _bob),
        });

        var chars = svc.Compute(c);
        chars.Power.Should().Be(3, "attacking it gets +2/+0");
        chars.Toughness.Should().Be(1);
    }

    [Fact]
    public void AdantoVanguard_PayLife_GrantsIndestructibleEOT()
    {
        var svc = new ContinuousEffectsService();
        var combat = new CombatManager();
        var c = AdantoVanguardFactory.Create(_alice, svc, combat);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        svc.Compute(c).Keywords.Should().NotContain("Indestructible");

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ability.Costs.OfType<PayLifeCost>().Single();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice);
        _alice.LifeTotal.Should().Be(16);

        foreach (var e in ability.Effects) e.Execute();

        svc.Compute(c).Keywords.Should().Contain("Indestructible");
    }

    [Fact]
    public void AdantoVanguard_PayLife_CannotPayWithoutEnoughLife()
    {
        var lowLife = new Player("Low", 3);
        var c = AdantoVanguardFactory.Create(lowLife);

        var cost = c.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<PayLifeCost>().Single();

        cost.CanPay(lowLife).Should().BeFalse("CR 119.4 — can't pay 4 life with only 3");
    }
}
