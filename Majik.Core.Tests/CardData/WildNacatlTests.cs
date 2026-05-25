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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WildNacatlFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/1, Cat + Warrior subtypes,
///   mana cost {G}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Conditional Layer 7c pump:
///   - No Mountain or Plains  → 1/1.
///   - Mountain only          → 2/2.
///   - Plains only            → 2/2.
///   - Both Mountain + Plains → 3/3.
///   - Duplicate Mountains    → 2/2 (gate is boolean).
///   - Opponent's lands       → don't count.
///   - Off-battlefield Nacatl → no pump (effect not registered).
/// </summary>
public class WildNacatlTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly ContinuousEffectsService _effects = new();
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public WildNacatlTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WildNacatl_Identity()
    {
        var nacatl = WildNacatlFactory.Create(_alice);

        nacatl.Name.Should().Be("Wild Nacatl");
        nacatl.HasType(CardType.Creature).Should().BeTrue();
        nacatl.Power.Should().Be(1);
        nacatl.Toughness.Should().Be(1);
        nacatl.HasSubtype(CardSubtype.Cat).Should().BeTrue("Wild Nacatl is a Cat (CR 205.3m)");
        nacatl.HasSubtype(CardSubtype.Warrior).Should().BeTrue("Wild Nacatl is also a Warrior (CR 205.3m)");
        nacatl.ManaCost.Should().Be("{G}");
        nacatl.Owner.Should().BeSameAs(_alice);
        nacatl.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WildNacatl_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Wild Nacatl", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Wild Nacatl");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.ManaCost.Should().Be("{G}");
    }

    // -----------------------------------------------------------------------
    // Conditional pump — Layer 7c static effects
    // -----------------------------------------------------------------------

    [Fact]
    public void WildNacatl_NoMountainOrPlains_IsBaseStatLine()
    {
        var nacatl = WildNacatlFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(nacatl, ZoneType.Library, ZoneType.Battlefield, _alice);

        var chars = _effects.Compute(nacatl);

        chars.Power.Should().Be(1, "no Mountain or Plains → base 1/1");
        chars.Toughness.Should().Be(1, "no Mountain or Plains → base 1/1");
    }

    [Fact]
    public void WildNacatl_MountainOnly_IsTwoTwo()
    {
        var nacatl = WildNacatlFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(nacatl, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_alice, CardSubtype.Mountain);

        var chars = _effects.Compute(nacatl);

        chars.Power.Should().Be(2, "Mountain triggers +1/+1 → 2/2");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void WildNacatl_PlainsOnly_IsTwoTwo()
    {
        var nacatl = WildNacatlFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(nacatl, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_alice, CardSubtype.Plains);

        var chars = _effects.Compute(nacatl);

        chars.Power.Should().Be(2, "Plains triggers +1/+1 → 2/2");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void WildNacatl_MountainAndPlains_IsThreeThree()
    {
        var nacatl = WildNacatlFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(nacatl, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_alice, CardSubtype.Mountain);
        AddLand(_alice, CardSubtype.Plains);

        var chars = _effects.Compute(nacatl);

        chars.Power.Should().Be(3, "Mountain + Plains → both pumps stack → 3/3");
        chars.Toughness.Should().Be(3);
    }

    [Fact]
    public void WildNacatl_DuplicateMountains_StillTwoTwo()
    {
        // The gate is "you control A Mountain" — boolean predicate, not a count.
        // Two Mountains should still yield only +1/+1.
        var nacatl = WildNacatlFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(nacatl, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_alice, CardSubtype.Mountain);
        AddLand(_alice, CardSubtype.Mountain);

        var chars = _effects.Compute(nacatl);

        chars.Power.Should().Be(2, "predicate is boolean — 2× Mountain doesn't double the pump");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void WildNacatl_OpponentLandsDoNotCount()
    {
        // Bob's Mountain & Plains should not boost Alice's Nacatl.
        var nacatl = WildNacatlFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(nacatl, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_bob, CardSubtype.Mountain);
        AddLand(_bob, CardSubtype.Plains);

        var chars = _effects.Compute(nacatl);

        chars.Power.Should().Be(1, "opponent's lands don't satisfy 'you control a Mountain/Plains'");
        chars.Toughness.Should().Be(1);
    }

    [Fact]
    public void WildNacatl_NotOnBattlefield_NoPumpRegistered()
    {
        // Nacatl in library — lifecycle hasn't fired the ETB CardMovedEvent,
        // so neither pump is registered.
        var nacatl = WildNacatlFactory.Create(_alice, _effects, _bus);

        AddLand(_alice, CardSubtype.Mountain);
        AddLand(_alice, CardSubtype.Plains);

        var chars = _effects.Compute(nacatl);

        chars.Power.Should().Be(1, "pumps not registered while Nacatl is off-battlefield");
        chars.Toughness.Should().Be(1);
    }

    [Fact]
    public void WildNacatl_LeavesBattlefield_PumpsUnregister()
    {
        var nacatl = WildNacatlFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(nacatl, ZoneType.Library, ZoneType.Battlefield, _alice);
        AddLand(_alice, CardSubtype.Mountain);
        AddLand(_alice, CardSubtype.Plains);

        // Confirm pumps active.
        _effects.Compute(nacatl).Power.Should().Be(3);

        // Move Nacatl to graveyard — lifecycle should unregister both pumps.
        _zones.MoveCard(nacatl, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        // After LTB, effect should not apply (IsActive gate also false).
        var chars = _effects.Compute(nacatl);
        chars.Power.Should().Be(1, "pumps unregistered after LTB");
        chars.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ControlsBasicLandType helper
    // -----------------------------------------------------------------------

    [Fact]
    public void ControlsBasicLandType_TrueWhenPresent()
    {
        AddLand(_alice, CardSubtype.Mountain);
        WildNacatlFactory.ControlsBasicLandType(_alice, CardSubtype.Mountain).Should().BeTrue();
    }

    [Fact]
    public void ControlsBasicLandType_FalseWhenAbsent()
    {
        WildNacatlFactory.ControlsBasicLandType(_alice, CardSubtype.Mountain).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void AddLand(Player controller, CardSubtype subtype)
    {
        var land = new Land(subtype.ToString(), supertypes: null, subtypes: new[] { subtype });
        land.SetOwner(controller);
        land.SetController(controller);
        controller.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }
}
