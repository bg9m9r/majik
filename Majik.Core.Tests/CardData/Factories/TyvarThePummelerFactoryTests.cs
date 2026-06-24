using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tyvar, the Pummeler (Legendary Creature — Elf Warrior, {1}{G}{G}).
///
/// Oracle (verified against Scryfall):
///   "Tap another untapped creature you control: Tyvar gains indestructible
///    until end of turn. Tap it.
///    {3}{G}{G}: Creatures you control get +X/+X until end of turn, where X
///    is the greatest power among creatures you control."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity: name, Legendary supertype, Elf + Warrior subtypes, 3/3, {1}{G}{G}.
///   - Two activated abilities: tap-a-creature (no mana) + {3}{G}{G} pump.
///   - Tap-another-creature cost grants Tyvar Indestructible EOT and taps the
///     creature (CR 118.12 tap-as-cost).
///   - Cost can't be paid with no OTHER untapped creature (CR 119.4).
///   - {3}{G}{G} team pump = greatest power among creatures you control (CR 608.2).
/// (NamedCardFactory dispatch + well-formedness covered by CardFactoryContractTests.)
/// </summary>
[Trait("Color", "G")]
public class TyvarThePummelerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature Vanilla(Player owner, int power, int toughness, ContinuousEffectsService svc)
    {
        var c = new Creature("Bear", "{1}{G}", power, toughness);
        c.ActiveEffects = svc;
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.ClearSummoningSickness();
        return c;
    }

    [Fact]
    public void Tyvar_IsLegendaryElfWarrior_3_3_AtCost1GG()
    {
        var c = TyvarThePummelerFactory.Create(_alice);

        c.Name.Should().Be("Tyvar, the Pummeler");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
    }

    [Fact]
    public void Tyvar_HasTapAbility_AndManaPumpAbility()
    {
        var c = TyvarThePummelerFactory.Create(_alice);

        var abilities = c.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(2);

        abilities.Should().ContainSingle(a =>
            a.Costs.OfType<TapAnotherUntappedCreatureCost>().Any());
        abilities.Should().ContainSingle(a =>
            a.Costs.OfType<ManaCostCost>().Any());
    }

    [Fact]
    public void Tyvar_TapAnotherCreature_GrantsIndestructibleEOT_AndTapsIt()
    {
        var svc = new ContinuousEffectsService();
        var tyvar = TyvarThePummelerFactory.Create(_alice);
        tyvar.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(tyvar);
        tyvar.SetZone(ZoneType.Battlefield);

        var helper = Vanilla(_alice, 2, 2, svc);

        svc.Compute(tyvar).Keywords.Should().NotContain("Indestructible");

        var ability = tyvar.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<TapAnotherUntappedCreatureCost>().Any());
        var cost = ability.Costs.OfType<TapAnotherUntappedCreatureCost>().Single();

        cost.CanPay(_alice).Should().BeTrue("there is another untapped creature to tap");
        cost.Pay(_alice);
        helper.IsTapped.Should().BeTrue("the cost taps another untapped creature (CR 118.12)");

        foreach (var e in ability.Effects) e.Execute();

        svc.Compute(tyvar).Keywords.Should().Contain("Indestructible");
    }

    [Fact]
    public void Tyvar_TapAbility_CannotPay_WithNoOtherUntappedCreature()
    {
        var svc = new ContinuousEffectsService();
        var tyvar = TyvarThePummelerFactory.Create(_alice);
        tyvar.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(tyvar);
        tyvar.SetZone(ZoneType.Battlefield);

        var cost = tyvar.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<TapAnotherUntappedCreatureCost>().Any())
            .Costs.OfType<TapAnotherUntappedCreatureCost>().Single();

        cost.CanPay(_alice).Should().BeFalse(
            "CR 119.4 — Tyvar is the only creature; 'another' creature is required");
    }

    [Fact]
    public void Tyvar_TeamPump_GivesPlusXPlusX_WhereXIsGreatestPower()
    {
        var svc = new ContinuousEffectsService();
        var tyvar = TyvarThePummelerFactory.Create(_alice); // base 3/3
        tyvar.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(tyvar);
        tyvar.SetZone(ZoneType.Battlefield);

        var small = Vanilla(_alice, 1, 1, svc);
        var big = Vanilla(_alice, 5, 4, svc); // greatest power = 5

        var ability = tyvar.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        foreach (var e in ability.Effects) e.Execute();

        // X = 5 (greatest power among creatures you control). Each creature
        // gets +5/+5 until end of turn (CR 608.2 / 613.1c).
        svc.Compute(tyvar).Power.Should().Be(8);   // 3 + 5
        svc.Compute(tyvar).Toughness.Should().Be(8);
        svc.Compute(small).Power.Should().Be(6);   // 1 + 5
        svc.Compute(big).Power.Should().Be(10);    // 5 + 5
        svc.Compute(big).Toughness.Should().Be(9); // 4 + 5
    }
}
