using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.Vehicles;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Cultivator's Caravan (Kaladesh, {3}, Artifact — Vehicle 5/5).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color."
///   "Crew 3 (Tap any number of creatures you control with total power 3 or
///    more: This Vehicle becomes an artifact creature until end of turn.)"
///
/// Covers:
///   - Identity (Artifact + Creature shell, Vehicle subtype, 5/5, {3},
///     owner/controller, non-legendary).
///   - NamedCardFactory dispatches via the [CardName] generator.
///   - Five free coloured mana abilities — the JSON encoding of
///     "Add one mana of any color" (CR 605.1), same posture as Chromatic
///     Star / Springleaf Drum; one ManaAbility per WUBRG, each free
///     (no {1} cost, unlike Prismatic Lens).
///   - Each coloured ability adds exactly its colour and taps the caravan.
///   - Crew 3 (CR 702.122) promotes the vehicle to a 5/5 creature via
///     VehicleCrewEffect.
/// </summary>
public class CultivatorsCaravanFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CultivatorsCaravan_Identity()
    {
        var c = CultivatorsCaravanFactory.Create(_alice);

        c.Name.Should().Be("Cultivator's Caravan");
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Cultivator's Caravan is an Artifact (Vehicle)");
        c.HasType(CardType.Creature).Should().BeTrue(
            "v1 vehicle shell is a Creature so CrewAction flows P/T through " +
            "VehicleCrewEffect");
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.ManaCost.Should().Be("{3}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CultivatorsCaravan_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Cultivator's Caravan", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Cultivator's Caravan");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // "{T}: Add one mana of any color" — five free coloured mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void CultivatorsCaravan_HasFiveColoredManaAbilities()
    {
        var c = CultivatorsCaravanFactory.Create(_alice);

        var mana = c.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(
            5,
            "one ManaAbility per WUBRG encodes 'Add one mana of any color' " +
            "(CR 605.1)");
    }

    [Fact]
    public void CultivatorsCaravan_HasOneAbilityPerColor_ProducingThatColor()
    {
        var c = CultivatorsCaravanFactory.Create(_alice);
        var mana = c.Abilities.OfType<ManaAbility>().ToList();

        mana.Count(a => a.ManaGenerated.White == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Blue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Black == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Red == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Green == 1).Should().Be(1);
    }

    [Fact]
    public void CultivatorsCaravan_ColoredAbilities_AreFree_CanActivateWithEmptyPool()
    {
        var c = CultivatorsCaravanFactory.Create(_alice);
        // CR 302.6 / 605.3a — the v1 Vehicle shell is a Creature, so its {T}
        // mana ability is subject to summoning sickness. Clear it to model a
        // permanent that has been under its controller's control (the normal
        // case for tapping a mana source). No additional MANA cost gates the
        // ability (unlike Prismatic Lens's {1}).
        c.HasSummoningSickness = false;

        foreach (var ability in c.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeTrue(
                "'{T}: Add one mana of any color' carries no additional mana " +
                "cost (unlike Prismatic Lens's {1})");
        }
    }

    [Fact]
    public void CultivatorsCaravan_GreenActivation_AddsGreen_AndTapsSelf()
    {
        var c = CultivatorsCaravanFactory.Create(_alice);
        // CR 302.6 / 605.3a — clear summoning sickness (Creature shell) so the
        // {T} mana ability is activatable.
        c.HasSummoningSickness = false;
        var green = c.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Green == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(green, _alice);

        _alice.ManaPool.Green.Should().Be(1);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Blue.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0);
        c.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    [Fact]
    public void CultivatorsCaravan_HasNoActivatedOrTriggeredAbilities()
    {
        var c = CultivatorsCaravanFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only printed activated ability is the mana ability");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Crew 3 (CR 702.122) — drives the existing VehicleCrewEffect machinery.
    // -----------------------------------------------------------------------

    [Fact]
    public void CultivatorsCaravan_Crew3_PromotesToCreatureUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var caravan = CultivatorsCaravanFactory.Create(_alice);
        caravan.ActiveEffects = effects;
        caravan.HasSummoningSickness = false;

        // One 3-power creature satisfies total power 3.
        var crew = new Creature("Ox", "2G", 3, 3)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            caravan,
            crewCost: CultivatorsCaravanFactory.CrewCost,
            vehiclePower: CultivatorsCaravanFactory.VehiclePower,
            vehicleToughness: CultivatorsCaravanFactory.VehicleToughness,
            new[] { crew },
            effects);

        result.Success.Should().BeTrue("3 power ≥ crew cost 3");
        crew.IsTapped.Should().BeTrue("crewmates tap to crew");
        caravan.Power.Should().Be(5, "VehicleCrewEffect ships base 5 through Layer 7b");
        caravan.Toughness.Should().Be(5);
    }

    [Fact]
    public void CultivatorsCaravan_Crew3_FailsWhenTotalPowerTooLow()
    {
        var effects = new ContinuousEffectsService();
        var caravan = CultivatorsCaravanFactory.Create(_alice);
        caravan.ActiveEffects = effects;

        var weak = new Creature("Mouse", "W", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            caravan,
            crewCost: CultivatorsCaravanFactory.CrewCost,
            vehiclePower: CultivatorsCaravanFactory.VehiclePower,
            vehicleToughness: CultivatorsCaravanFactory.VehicleToughness,
            new[] { weak },
            effects);

        result.Success.Should().BeFalse("2 power < crew cost 3");
    }
}
