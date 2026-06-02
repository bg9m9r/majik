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
/// Unit tests for <see cref="LoamLionFactory"/>.
///
/// Loam Lion is mechanically identical to Kird Ape — "+1/+2 as long as you
/// control a Forest" — differing only in name, color ({W}) and subtype (Cat).
/// These tests mirror <see cref="KirdApeFactoryTests"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/1, Cat subtype, mana cost {W},
///   owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Forest-conditional self-pump (Layer 7c):
///   - 0 Forests → 1/1.
///   - 1 Forest → 2/3 (+1/+2).
///   - 2 Forests → 2/3 (flat bonus, not per-Forest).
///   - Pump dynamically re-evaluates as a Forest ETBs / LTBs.
///   - Only the controller's Forests count.
///   - Non-Forest lands (Mountain) do not trigger the bonus.
/// - Helper predicate (ControlsForest).
/// </summary>
[Trait("Color", "W")]
public class LoamLionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land NewForest(Player owner, string name = "Forest")
    {
        var f = new Land(name, subtypes: new[] { CardSubtype.Forest });
        f.SetOwner(owner);
        f.SetController(owner);
        f.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(f);
        return f;
    }

    private static Land NewMountain(Player owner, string name = "Mountain")
    {
        var m = new Land(name, subtypes: new[] { CardSubtype.Mountain });
        m.SetOwner(owner);
        m.SetController(owner);
        m.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(m);
        return m;
    }

    [Fact]
    public void LoamLion_Identity()
    {
        var lion = LoamLionFactory.Create(_alice);

        lion.Name.Should().Be("Loam Lion");
        lion.ManaCost.Should().Be("{W}");
        lion.HasType(CardType.Creature).Should().BeTrue();
        lion.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        lion.BasePower.Should().Be(1);
        lion.BaseToughness.Should().Be(1);
        lion.Owner.Should().BeSameAs(_alice);
        lion.Controller.Should().BeSameAs(_alice);
    }
    private Creature NewLionOnBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var lion = LoamLionFactory.Create(_alice, effects, bus);
        zones.MoveCard(lion, ZoneType.Library, ZoneType.Battlefield, _alice);
        lion.ActiveEffects = effects;
        return lion;
    }

    [Fact]
    public void Forest_ZeroForests_StaysOneOne()
    {
        var lion = NewLionOnBattlefield();
        lion.Power.Should().Be(1);
        lion.Toughness.Should().Be(1);
    }

    [Fact]
    public void Forest_OneForest_ActivatesBonus_TwoThree()
    {
        var lion = NewLionOnBattlefield();
        NewForest(_alice);

        lion.Power.Should().Be(2, "1 + 1 Forest bonus");
        lion.Toughness.Should().Be(3, "1 + 2 Forest bonus");
    }

    [Fact]
    public void Forest_TwoForests_NoExtraStacking_TwoThree()
    {
        var lion = NewLionOnBattlefield();
        NewForest(_alice, "F1");
        NewForest(_alice, "F2");

        lion.Power.Should().Be(2, "+1/+2 is a flat bonus, not per-Forest");
        lion.Toughness.Should().Be(3);
    }

    [Fact]
    public void Forest_NonForestLand_DoesNotActivate()
    {
        var lion = NewLionOnBattlefield();
        NewMountain(_alice);

        lion.Power.Should().Be(1, "a Mountain is not a Forest");
        lion.Toughness.Should().Be(1);
    }

    [Fact]
    public void Forest_DynamicallyReevaluates_OnForestComingAndGoing()
    {
        var lion = NewLionOnBattlefield();

        // No Forest yet.
        lion.Power.Should().Be(1);

        // A Forest arrives → bonus flips on. The bystander Forest is added via
        // raw zone ops (no ActiveEffects wired), so invalidate the layer-system
        // cache explicitly via Clear() — production's CardMovedEvent does this.
        var forest = NewForest(_alice);
        lion.ActiveEffects!.Clear();
        lion.Power.Should().Be(2);
        lion.Toughness.Should().Be(3);

        // Last Forest leaves → bonus flips off.
        _alice.Zones.Battlefield.RemoveCard(forest);
        forest.SetZone(ZoneType.Graveyard);
        lion.ActiveEffects!.Clear();
        lion.Power.Should().Be(1);
        lion.Toughness.Should().Be(1);
    }

    [Fact]
    public void Forest_OpponentsForestsDoNotCount()
    {
        var lion = NewLionOnBattlefield();
        NewForest(_bob, "B1");

        lion.Power.Should().Be(1,
            "the bonus reads 'YOU control a Forest', not opponent's");
        lion.Toughness.Should().Be(1);
    }

    [Fact]
    public void ControlsForest_HelperPredicate()
    {
        LoamLionFactory.ControlsForest(_alice).Should().BeFalse();

        NewMountain(_alice);
        LoamLionFactory.ControlsForest(_alice).Should().BeFalse(
            "a Mountain is not a Forest");

        NewForest(_alice);
        LoamLionFactory.ControlsForest(_alice).Should().BeTrue();
    }
}
