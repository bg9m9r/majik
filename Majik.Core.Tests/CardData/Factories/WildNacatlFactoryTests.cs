using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WildNacatlFactory"/>.
///
/// Wild Nacatl ({G}, Creature — Cat Warrior 1/1). Oracle text:
///   "This creature gets +1/+1 as long as you control a Mountain.
///    This creature gets +1/+1 as long as you control a Plains."
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/1, Cat + Warrior subtypes,
///   mana cost {G}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Two independent Layer 7c conditional self-pumps (CR 613.7c):
///   - No Mountain, no Plains -> 1/1.
///   - Mountain only -> 2/2.
///   - Plains only -> 2/2.
///   - Mountain + Plains -> 3/3.
///   - Two Mountains -> 2/2 (each clause is a flat +1/+1, not per-land).
///   - Dynamic re-evaluation as lands ETB / LTB.
///   - Only the controller's lands count.
/// </summary>
public class WildNacatlFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land NewBasic(Player owner, CardSubtype landType, string name)
    {
        var land = new Land(name, subtypes: new[] { landType });
        land.SetOwner(owner);
        land.SetController(owner);
        land.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }

    [Fact]
    public void WildNacatl_Identity()
    {
        var n = WildNacatlFactory.Create(_alice);

        n.Name.Should().Be("Wild Nacatl");
        n.ManaCost.Should().Be("{G}");
        n.HasType(CardType.Creature).Should().BeTrue();
        n.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        n.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        n.BasePower.Should().Be(1);
        n.BaseToughness.Should().Be(1);
        n.Owner.Should().BeSameAs(_alice);
        n.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WildNacatl_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Wild Nacatl", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Wild Nacatl");
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    private (Creature nacatl, ContinuousEffectsService effects) NewNacatlOnBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var nacatl = WildNacatlFactory.Create(_alice, effects, bus);
        zones.MoveCard(nacatl, ZoneType.Library, ZoneType.Battlefield, _alice);
        nacatl.ActiveEffects = effects;
        return (nacatl, effects);
    }

    [Fact]
    public void NoLands_StaysOneOne()
    {
        var (n, _) = NewNacatlOnBattlefield();
        n.Power.Should().Be(1);
        n.Toughness.Should().Be(1);
    }

    [Fact]
    public void MountainOnly_PumpsTwoTwo()
    {
        var (n, _) = NewNacatlOnBattlefield();
        NewBasic(_alice, CardSubtype.Mountain, "Mountain");

        n.Power.Should().Be(2, "1 + 1 for controlling a Mountain");
        n.Toughness.Should().Be(2);
    }

    [Fact]
    public void PlainsOnly_PumpsTwoTwo()
    {
        var (n, _) = NewNacatlOnBattlefield();
        NewBasic(_alice, CardSubtype.Plains, "Plains");

        n.Power.Should().Be(2, "1 + 1 for controlling a Plains");
        n.Toughness.Should().Be(2);
    }

    [Fact]
    public void MountainAndPlains_PumpsThreeThree()
    {
        var (n, _) = NewNacatlOnBattlefield();
        NewBasic(_alice, CardSubtype.Mountain, "Mountain");
        NewBasic(_alice, CardSubtype.Plains, "Plains");

        n.Power.Should().Be(3, "1 + 1 Mountain + 1 Plains");
        n.Toughness.Should().Be(3);
    }

    [Fact]
    public void TwoMountains_OnlyOneBonus_TwoTwo()
    {
        var (n, _) = NewNacatlOnBattlefield();
        NewBasic(_alice, CardSubtype.Mountain, "Mountain1");
        NewBasic(_alice, CardSubtype.Mountain, "Mountain2");

        n.Power.Should().Be(2, "the Mountain clause is a flat +1/+1, not per-Mountain");
        n.Toughness.Should().Be(2);
    }

    [Fact]
    public void DynamicallyReevaluates_OnLandComingAndGoing()
    {
        var (n, _) = NewNacatlOnBattlefield();

        n.Power.Should().Be(1);

        var mountain = NewBasic(_alice, CardSubtype.Mountain, "Mountain");
        n.Power.Should().Be(2);

        var plains = NewBasic(_alice, CardSubtype.Plains, "Plains");
        n.Power.Should().Be(3);
        n.Toughness.Should().Be(3);

        _alice.Zones.Battlefield.RemoveCard(mountain);
        mountain.SetZone(ZoneType.Graveyard);
        n.Power.Should().Be(2, "Mountain gone -> only the Plains clause remains");

        _alice.Zones.Battlefield.RemoveCard(plains);
        plains.SetZone(ZoneType.Graveyard);
        n.Power.Should().Be(1);
        n.Toughness.Should().Be(1);
    }

    [Fact]
    public void OpponentsLandsDoNotCount()
    {
        var (n, _) = NewNacatlOnBattlefield();
        NewBasic(_bob, CardSubtype.Mountain, "Mountain");
        NewBasic(_bob, CardSubtype.Plains, "Plains");

        n.Power.Should().Be(1,
            "the clauses read 'YOU control', not the opponent's lands");
        n.Toughness.Should().Be(1);
    }

    [Fact]
    public void HelperPredicates()
    {
        WildNacatlFactory.ControlsLandSubtype(_alice, CardSubtype.Mountain, effects: null)
            .Should().BeFalse();
        WildNacatlFactory.ControlsLandSubtype(_alice, CardSubtype.Plains, effects: null)
            .Should().BeFalse();

        NewBasic(_alice, CardSubtype.Mountain, "Mountain");
        WildNacatlFactory.ControlsLandSubtype(_alice, CardSubtype.Mountain, effects: null)
            .Should().BeTrue();
        WildNacatlFactory.ControlsLandSubtype(_alice, CardSubtype.Plains, effects: null)
            .Should().BeFalse();
    }
}
