using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FlinthoofBoarFactory"/>.
///
/// Flinthoof Boar (Magic 2013, {1}{G}) is a 2/2 Creature — Boar with two
/// printed behaviours (Scryfall verified 2026-06):
///   "This creature gets +1/+1 as long as you control a Mountain.
///    {R}: This creature gains haste until end of turn."
///
/// The Mountain-conditional pump mirrors <see cref="KirdApeFactory"/> /
/// <see cref="LoamLionFactory"/> (a Forest pump) — only the land subtype and
/// the bonus magnitude differ. The {R} self-haste grant mirrors
/// <see cref="WerewolfPackLeaderFactory"/>'s EOT activated grant.
///
/// Covers:
/// - Identity (name, type Creature, P/T 2/2, Boar subtype, mana cost {1}{G},
///   owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Mountain-conditional self-pump (Layer 7c):
///   - 0 Mountains → 2/2.
///   - 1 Mountain → 3/3 (+1/+1).
///   - 2 Mountains → 3/3 (flat bonus, not per-Mountain).
///   - Pump dynamically re-evaluates as a Mountain ETBs / LTBs.
///   - Only the controller's Mountains count.
///   - Non-Mountain lands (Forest) do not trigger the bonus.
/// - {R} activated ability grants Haste until end of turn.
/// - Helper predicate (ControlsMountain).
/// </summary>
[Trait("Color", "GR")]
public class FlinthoofBoarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land NewMountain(Player owner, string name = "Mountain")
    {
        var m = new Land(name, subtypes: new[] { CardSubtype.Mountain });
        m.SetOwner(owner);
        m.SetController(owner);
        m.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(m);
        return m;
    }

    private static Land NewForest(Player owner, string name = "Forest")
    {
        var f = new Land(name, subtypes: new[] { CardSubtype.Forest });
        f.SetOwner(owner);
        f.SetController(owner);
        f.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(f);
        return f;
    }

    [Fact]
    public void FlinthoofBoar_Identity()
    {
        var boar = FlinthoofBoarFactory.Create(_alice);

        boar.Name.Should().Be("Flinthoof Boar");
        boar.ManaCost.Should().Be("{1}{G}");
        boar.HasType(CardType.Creature).Should().BeTrue();
        boar.HasSubtype(CardSubtype.Boar).Should().BeTrue();
        boar.BasePower.Should().Be(2);
        boar.BaseToughness.Should().Be(2);
        boar.Owner.Should().BeSameAs(_alice);
        boar.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedFactory_Dispatches_FlinthoofBoar()
    {
        var card = NamedCardFactory.Create("Flinthoof Boar", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Flinthoof Boar");
        card.HasSubtype(CardSubtype.Boar).Should().BeTrue();
    }

    private Creature NewBoarOnBattlefield(out ContinuousEffectsService effects)
    {
        effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var boar = FlinthoofBoarFactory.Create(_alice, effects, bus);
        zones.MoveCard(boar, ZoneType.Library, ZoneType.Battlefield, _alice);
        boar.ActiveEffects = effects;
        return boar;
    }

    [Fact]
    public void Mountain_ZeroMountains_StaysTwoTwo()
    {
        var boar = NewBoarOnBattlefield(out _);
        boar.Power.Should().Be(2);
        boar.Toughness.Should().Be(2);
    }

    [Fact]
    public void Mountain_OneMountain_ActivatesBonus_ThreeThree()
    {
        var boar = NewBoarOnBattlefield(out _);
        NewMountain(_alice);

        boar.Power.Should().Be(3, "2 + 1 Mountain bonus");
        boar.Toughness.Should().Be(3, "2 + 1 Mountain bonus");
    }

    [Fact]
    public void Mountain_TwoMountains_NoExtraStacking_ThreeThree()
    {
        var boar = NewBoarOnBattlefield(out _);
        NewMountain(_alice, "M1");
        NewMountain(_alice, "M2");

        boar.Power.Should().Be(3, "+1/+1 is a flat bonus, not per-Mountain");
        boar.Toughness.Should().Be(3);
    }

    [Fact]
    public void Mountain_NonMountainLand_DoesNotActivate()
    {
        var boar = NewBoarOnBattlefield(out _);
        NewForest(_alice);

        boar.Power.Should().Be(2, "a Forest is not a Mountain");
        boar.Toughness.Should().Be(2);
    }

    [Fact]
    public void Mountain_DynamicallyReevaluates_OnMountainComingAndGoing()
    {
        var boar = NewBoarOnBattlefield(out _);

        boar.Power.Should().Be(2);

        // A Mountain arrives → bonus flips on. The bystander Mountain is added
        // via raw zone ops (no ActiveEffects wired), so invalidate the layer
        // cache explicitly via Clear() — production's CardMovedEvent does this.
        var mountain = NewMountain(_alice);
        boar.ActiveEffects!.Clear();
        boar.Power.Should().Be(3);
        boar.Toughness.Should().Be(3);

        // Last Mountain leaves → bonus flips off.
        _alice.Zones.Battlefield.RemoveCard(mountain);
        mountain.SetZone(ZoneType.Graveyard);
        boar.ActiveEffects!.Clear();
        boar.Power.Should().Be(2);
        boar.Toughness.Should().Be(2);
    }

    [Fact]
    public void Mountain_OpponentsMountainsDoNotCount()
    {
        var boar = NewBoarOnBattlefield(out _);
        NewMountain(_bob, "B1");

        boar.Power.Should().Be(2,
            "the bonus reads 'YOU control a Mountain', not opponent's");
        boar.Toughness.Should().Be(2);
    }

    [Fact]
    public void ControlsMountain_HelperPredicate()
    {
        FlinthoofBoarFactory.ControlsMountain(_alice).Should().BeFalse();

        NewForest(_alice);
        FlinthoofBoarFactory.ControlsMountain(_alice).Should().BeFalse(
            "a Forest is not a Mountain");

        NewMountain(_alice);
        FlinthoofBoarFactory.ControlsMountain(_alice).Should().BeTrue();
    }

    // ── {R} self-haste activated ability ─────────────────────────────────

    [Fact]
    public void HasteAbility_CostIsR_AndIsActivated()
    {
        var boar = FlinthoofBoarFactory.Create(_alice);

        var activated = boar.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = activated.Costs.OfType<ManaCostCost>().Single();
        manaCost.Description.Should().Contain("R");
    }

    [Fact]
    public void HasteAbility_GrantsHasteUntilEndOfTurn()
    {
        var boar = NewBoarOnBattlefield(out var effects);

        // No haste before activating.
        effects.Compute(boar).Keywords.Should().NotContain(
            FlinthoofBoarFactory.Haste);

        var activated = boar.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        effects.Compute(boar).Keywords.Should().Contain(
            FlinthoofBoarFactory.Haste, "the {R} ability grants haste until end of turn.");
    }
}
