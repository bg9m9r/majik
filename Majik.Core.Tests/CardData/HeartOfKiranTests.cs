using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.Vehicles;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Heart of Kiran (Aether Revolt, {2}, Legendary Artifact —
/// Vehicle 4/4).
///
/// Covers:
///   - Identity (Legendary + Artifact + Creature, Vehicle subtype, 4/4, {2}).
///   - NamedCardFactory dispatches via the [CardName] generator.
///   - Flying + Vigilance keyword markers attached.
///   - Crew 3 promotes via VehicleCrewEffect.
///   - Alt-crew (remove loyalty from your planeswalker) promotes the
///     vehicle to a 4/4 creature AND strips one loyalty counter.
///   - Alt-crew refuses: opponent's planeswalker / zero-loyalty / off-bf.
/// </summary>
public class HeartOfKiranTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void HeartOfKiran_Identity()
    {
        var c = HeartOfKiranFactory.Create(_alice);

        c.Name.Should().Be("Heart of Kiran");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue(
            "v1 vehicle shell is a Creature so CrewAction flows P/T through " +
            "VehicleCrewEffect");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        c.ManaCost.Should().Be("{2}");
    }

    [Fact]
    public void HeartOfKiran_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Heart of Kiran", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Heart of Kiran");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
    }

    [Fact]
    public void HeartOfKiran_HasFlyingAndVigilance()
    {
        var c = HeartOfKiranFactory.Create(_alice);
        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().Contain("Flying", "CR 702.9 — Flying marker attached");
        keywords.Should().Contain("Vigilance", "CR 702.20 — Vigilance marker attached");
    }

    // -----------------------------------------------------------------------
    // Crew 3 (CR 702.122)
    // -----------------------------------------------------------------------

    [Fact]
    public void HeartOfKiran_Crew3_PromotesToCreatureUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var kiran = HeartOfKiranFactory.Create(_alice);
        kiran.ActiveEffects = effects;
        kiran.HasSummoningSickness = false;

        var crew = new Creature("Pilot", "1WW", 3, 3)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            kiran,
            crewCost: HeartOfKiranFactory.CrewCost,
            vehiclePower: HeartOfKiranFactory.VehiclePower,
            vehicleToughness: HeartOfKiranFactory.VehicleToughness,
            new[] { crew },
            effects);

        result.Success.Should().BeTrue("3 power ≥ crew cost 3");
        kiran.Power.Should().Be(4, "VehicleCrewEffect ships base 4 through Layer 7b");
        kiran.Toughness.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // Alt-crew cost — remove loyalty from your planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void HeartOfKiran_CrewByLoyalty_RemovesOneCounter_AndPromotes()
    {
        var effects = new ContinuousEffectsService();
        var kiran = HeartOfKiranFactory.Create(_alice);
        kiran.ActiveEffects = effects;

        var pw = new Planeswalker("Chandra, Token PW", "2RR", startingLoyalty: 4)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var result = HeartOfKiranFactory.CrewByRemovingLoyalty(kiran, pw, effects);

        result.Success.Should().BeTrue("alt-cost: remove one loyalty from " +
            "a planeswalker you control");
        pw.Loyalty.Should().Be(3, "exactly one loyalty counter removed (CR 122.1)");
        kiran.Power.Should().Be(4, "VehicleCrewEffect still ships base 4 (alt-cost " +
            "substitutes for the tap cost, not the crew effect)");
        kiran.Toughness.Should().Be(4);
    }

    [Fact]
    public void HeartOfKiran_CrewByLoyalty_OpponentPlaneswalker_Fails()
    {
        var effects = new ContinuousEffectsService();
        var kiran = HeartOfKiranFactory.Create(_alice);

        var pw = new Planeswalker("Chandra, Token PW", "2RR", startingLoyalty: 4)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var result = HeartOfKiranFactory.CrewByRemovingLoyalty(kiran, pw, effects);

        result.Success.Should().BeFalse(
            "the alt-cost is scoped to a planeswalker YOU control");
        pw.Loyalty.Should().Be(4, "no loyalty removed on failure");
    }

    [Fact]
    public void HeartOfKiran_CrewByLoyalty_ZeroLoyalty_Fails()
    {
        var effects = new ContinuousEffectsService();
        var kiran = HeartOfKiranFactory.Create(_alice);

        var pw = new Planeswalker("Chandra, Token PW", "2RR", startingLoyalty: 0)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var result = HeartOfKiranFactory.CrewByRemovingLoyalty(kiran, pw, effects);

        result.Success.Should().BeFalse(
            "0-loyalty planeswalker has no counter to remove");
    }
}
