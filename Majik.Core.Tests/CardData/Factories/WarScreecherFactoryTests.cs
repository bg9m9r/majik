using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WarScreecherFactory"/>.
///
/// War Screecher (Foundations Jumpstart, {1}{W}):
///   Creature — Bird 1/3.
///   "Flying
///    {5}{W}, {T}: Other creatures you control get +1/+1 until end of turn."
///
/// Covers:
///   - Identity (Bird 1/3, {1}{W}, owner/controller, Flying keyword stamped).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Activated ability shape: {5}{W} mana + {T} cost, NO target request
///     (controller-scoped mass pump, CR 611).
///   - Resolution: every OTHER creature the controller controls is pumped +1/+1;
///     the screecher itself ("OTHER", CR 601.2c) and opponents' creatures are
///     untouched; the pump expires at end of turn (CR 514.2).
///   - RebindSafe (re-homes via Agatha's Soul Cauldron).
/// </summary>
[Trait("Color", "W")]
public class WarScreecherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility PumpAbility(Creature screecher)
        => screecher.Abilities.OfType<ActivatedAbility>().Single();

    [Fact]
    public void WarScreecher_Identity()
    {
        var s = WarScreecherFactory.Create(_alice);

        s.Name.Should().Be("War Screecher");
        s.ManaCost.Should().Be("{1}{W}");
        s.HasType(CardType.Creature).Should().BeTrue();
        s.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        s.BasePower.Should().Be(1);
        s.BaseToughness.Should().Be(3);
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WarScreecher_HasFlyingKeyword()
    {
        var s = WarScreecherFactory.Create(_alice);

        s.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "War Screecher has Flying (CR 702.9)");
    }

    [Fact]
    public void WarScreecher_PumpAbility_HasManaAndTapCostAndNoTarget()
    {
        var s = WarScreecherFactory.Create(_alice);
        var ability = PumpAbility(s);

        ability.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("5") && c.Description.Contains("W"),
                "the activation costs {5}{W}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activation also taps War Screecher");
        ability.TargetRequests.Should().BeEmpty(
            "a controller-scoped mass pump (\"other creatures you control\") is non-targeted");
        ability.RebindSafe.Should().BeTrue(
            "the mass pump reads its scope off the (rebound) source's controller, so it re-homes via Agatha");
    }

    [Fact]
    public void WarScreecher_Pump_RaisesEveryOtherControlledCreature_NotSelfNotOpponents()
    {
        var bob = new Player("Bob", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var screecher = WarScreecherFactory.Create(_alice);
        Seat(screecher, _alice, effects, zones);

        var ally1 = Bear("Ally One", _alice, 2, 2);
        Seat(ally1, _alice, effects, zones);
        var ally2 = Bear("Ally Two", _alice, 3, 3);
        Seat(ally2, _alice, effects, zones);

        var enemy = Bear("Enemy Bear", bob, 2, 2);
        Seat(enemy, bob, effects, zones);

        var ability = PumpAbility(screecher);

        var ally1Before = ally1.GetPower();
        var ally2Before = ally2.GetPower();
        var screecherBefore = screecher.GetPower();
        var enemyBefore = enemy.GetPower();

        foreach (var e in ability.Effects) e.Execute();

        ally1.GetPower().Should().Be(ally1Before + 1, "an OTHER creature Alice controls is pumped");
        ally1.GetToughness().Should().Be(3, "+1/+1 raises a base-2/2 ally's toughness to 3");
        ally2.GetPower().Should().Be(ally2Before + 1, "the second ally is pumped too");
        screecher.GetPower().Should().Be(screecherBefore,
            "\"OTHER creatures you control\" (CR 601.2c) excludes War Screecher itself");
        enemy.GetPower().Should().Be(enemyBefore,
            "an opponent's creature is not \"a creature you control\" and is untouched");

        effects.ExpireEndOfTurn();
        ally1.GetPower().Should().Be(ally1Before, "the mass pump expires at end of turn (CR 514.2)");
        ally2.GetPower().Should().Be(ally2Before, "the mass pump expires at end of turn (CR 514.2)");
    }

    private static Creature Bear(string name, Player owner, int p, int t)
    {
        var c = new Creature(name, "1G", p, t);
        c.SetOwner(owner);
        c.ChangeController(owner);
        return c;
    }

    private static void Seat(
        Creature c, Player owner,
        ContinuousEffectsService effects, Majik.Core.Services.ZoneService zones)
    {
        c.SetOwner(owner);
        c.ChangeController(owner);
        owner.Zones.Library.AddCard(c);
        zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, owner);
        c.ActiveEffects = effects;
    }
}
