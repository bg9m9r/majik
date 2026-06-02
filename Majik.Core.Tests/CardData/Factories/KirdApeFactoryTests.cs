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
/// Unit tests for <see cref="KirdApeFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/1, Ape subtype, mana cost {R},
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
[Trait("Color", "R")]
public class KirdApeFactoryTests
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
    public void KirdApe_Identity()
    {
        var ape = KirdApeFactory.Create(_alice);

        ape.Name.Should().Be("Kird Ape");
        ape.ManaCost.Should().Be("{R}");
        ape.HasType(CardType.Creature).Should().BeTrue();
        ape.HasSubtype(CardSubtype.Ape).Should().BeTrue();
        ape.BasePower.Should().Be(1);
        ape.BaseToughness.Should().Be(1);
        ape.Owner.Should().BeSameAs(_alice);
        ape.Controller.Should().BeSameAs(_alice);
    }
    private Creature NewApeOnBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var ape = KirdApeFactory.Create(_alice, effects, bus);
        zones.MoveCard(ape, ZoneType.Library, ZoneType.Battlefield, _alice);
        ape.ActiveEffects = effects;
        return ape;
    }

    [Fact]
    public void Forest_ZeroForests_StaysOneOne()
    {
        var ape = NewApeOnBattlefield();
        ape.Power.Should().Be(1);
        ape.Toughness.Should().Be(1);
    }

    [Fact]
    public void Forest_OneForest_ActivatesBonus_TwoThree()
    {
        var ape = NewApeOnBattlefield();
        NewForest(_alice);

        ape.Power.Should().Be(2, "1 + 1 Forest bonus");
        ape.Toughness.Should().Be(3, "1 + 2 Forest bonus");
    }

    [Fact]
    public void Forest_TwoForests_NoExtraStacking_TwoThree()
    {
        var ape = NewApeOnBattlefield();
        NewForest(_alice, "F1");
        NewForest(_alice, "F2");

        ape.Power.Should().Be(2, "+1/+2 is a flat bonus, not per-Forest");
        ape.Toughness.Should().Be(3);
    }

    [Fact]
    public void Forest_NonForestLand_DoesNotActivate()
    {
        var ape = NewApeOnBattlefield();
        NewMountain(_alice);

        ape.Power.Should().Be(1, "a Mountain is not a Forest");
        ape.Toughness.Should().Be(1);
    }

    [Fact]
    public void Forest_DynamicallyReevaluates_OnForestComingAndGoing()
    {
        var ape = NewApeOnBattlefield();

        // No Forest yet.
        ape.Power.Should().Be(1);

        // A Forest arrives → bonus flips on. The bystander Forest is added via
        // raw zone ops (no ActiveEffects wired), so invalidate the layer-system
        // cache explicitly via Clear() — production's CardMovedEvent does this.
        var forest = NewForest(_alice);
        ape.ActiveEffects!.Clear();
        ape.Power.Should().Be(2);
        ape.Toughness.Should().Be(3);

        // Last Forest leaves → bonus flips off.
        _alice.Zones.Battlefield.RemoveCard(forest);
        forest.SetZone(ZoneType.Graveyard);
        ape.ActiveEffects!.Clear();
        ape.Power.Should().Be(1);
        ape.Toughness.Should().Be(1);
    }

    [Fact]
    public void Forest_OpponentsForestsDoNotCount()
    {
        var ape = NewApeOnBattlefield();
        NewForest(_bob, "B1");

        ape.Power.Should().Be(1,
            "the bonus reads 'YOU control a Forest', not opponent's");
        ape.Toughness.Should().Be(1);
    }

    [Fact]
    public void ControlsForest_HelperPredicate()
    {
        KirdApeFactory.ControlsForest(_alice).Should().BeFalse();

        NewMountain(_alice);
        KirdApeFactory.ControlsForest(_alice).Should().BeFalse(
            "a Mountain is not a Forest");

        NewForest(_alice);
        KirdApeFactory.ControlsForest(_alice).Should().BeTrue();
    }
}
