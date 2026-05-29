using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Skred (Coldsnap, {R}).
///
/// Oracle: "Skred deals damage to target creature equal to the number of
/// snow permanents you control."
///
/// Exercises:
///   * Instant shape (Red) + dispatch by name.
///   * Snow-permanent counting on the controller's battlefield (any type,
///     not just lands — CR 205.4d Snow supertype).
///   * N damage marked on the target creature.
///   * Zero snow permanents → clean no-op.
///   * Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class SkredFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Create_HasInstantShape_Red()
    {
        var skred = SkredFactory.Create(_alice);
        skred.Name.Should().Be("Skred");
        skred.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(skred).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsSkredShape()
    {
        var dispatched = NamedCardFactory.Create("Skred", _alice);
        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Skred");
    }

    [Fact]
    public void CountSnowPermanents_CountsAllSnowTypesControllerControls()
    {
        // Alice: 2 snow lands + 1 snow creature = 3 snow permanents.
        for (var i = 0; i < 2; i++)
        {
            var s = new Land("Snow-Covered Mountain",
                supertypes: new[] { CardSupertype.Snow },
                subtypes: new[] { CardSubtype.Mountain })
                { Owner = _alice, Controller = _alice };
            s.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(s);
        }
        var snowCreature = new Creature("Ice-Fang Coatl", "{G}{U}", 1, 1,
            supertypes: new[] { CardSupertype.Snow })
            { Owner = _alice, Controller = _alice };
        snowCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(snowCreature);

        // Alice also controls a non-snow land — must not count.
        var plainLand = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
        plainLand.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(plainLand);

        // Bob controls a snow land — must not count (controller-scoped).
        var bobSnow = new Land("Snow-Covered Island",
            supertypes: new[] { CardSupertype.Snow },
            subtypes: new[] { CardSubtype.Island })
            { Owner = _bob, Controller = _bob };
        bobSnow.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobSnow);

        SkredFactory.CountSnowPermanents(_alice).Should().Be(3);
        SkredFactory.CountSnowPermanents(_bob).Should().Be(1);
    }

    [Fact]
    public void CountSnowPermanents_NullController_IsZero()
    {
        SkredFactory.CountSnowPermanents(null!).Should().Be(0);
    }

    [Fact]
    public void Resolve_NoSnow_IsNoOp()
    {
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = SkredFactory.BuildSpellDefinition(_alice, o => o);
        var picks = new ChosenSpellParams(
            null, null,
            new[] { (IReadOnlyList<object>)new object[] { bobBear } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(picks)) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield);
        bobBear.Damage.Should().Be(0,
            because: "0 snow permanents → 0 damage");
    }

    [Fact]
    public void Resolve_WithSnow_DealsNDamage()
    {
        // Alice controls 4 snow lands.
        for (var i = 0; i < 4; i++)
        {
            var s = new Land("Snow-Covered Mountain",
                supertypes: new[] { CardSupertype.Snow },
                subtypes: new[] { CardSubtype.Mountain })
                { Owner = _alice, Controller = _alice };
            s.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(s);
        }

        var bobBeast = new Creature("Tarmogoyf", "{1}{G}", 5, 5)
            { Owner = _bob, Controller = _bob };
        bobBeast.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBeast);

        var def = SkredFactory.BuildSpellDefinition(_alice, o => o);
        var picks = new ChosenSpellParams(
            null, null,
            new[] { (IReadOnlyList<object>)new object[] { bobBeast } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(picks)) e.Execute();

        bobBeast.Damage.Should().Be(4,
            because: "Skred deals N damage where N = snow permanents Alice controls (4)");
    }

    [Fact]
    public void Resolve_TargetOffBattlefield_IsNoOp()
    {
        var s = new Land("Snow-Covered Mountain",
            supertypes: new[] { CardSupertype.Snow },
            subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
        s.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(s);

        var deadBear = new Creature("Bear", "{1}{G}", 2, 2)
            { Owner = _bob, Controller = _bob };
        deadBear.SetZone(ZoneType.Graveyard);

        var def = SkredFactory.BuildSpellDefinition(_alice, o => o);
        var picks = new ChosenSpellParams(
            null, null,
            new[] { (IReadOnlyList<object>)new object[] { deadBear } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(picks)) e.Execute();

        deadBear.Damage.Should().Be(0,
            because: "CR 608.2b — illegal target at resolution → no damage marked");
    }
}
