using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tomb of the Spirit Dragon (Modern Horizons 2) — Land.
///
/// Oracle text (verified against Scryfall 2026-06-23):
///   "{T}: Add {C}.
///    {2}, {T}: You gain 1 life for each colorless creature you control."
///
/// Exercises ONLY the card's unique behaviour:
///   * First ability ({T}: Add {C}) — wired from JSON.
///   * Second ability: ordinary {2},{T} activated ability (NOT a mana ability)
///     that gains 1 life per colorless creature the controller controls.
///   * Colour counting: only colourless creatures count; coloured creatures
///     and non-creatures (incl. Tomb itself) do not.
///   * N may be 0 (legal activation, no life gained).
/// </summary>
[Trait("Color", "C")]
public class TombOfTheSpiritDragonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Add a colourless creature (empty mana cost → no colours).</summary>
    private Creature AddColorlessCreature(Player controller, string name = "Spirit")
    {
        var c = new Creature(name, "0", 1, 1);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    /// <summary>Add a coloured creature ({G} cost → green, not colourless).</summary>
    private Creature AddGreenCreature(Player controller)
    {
        var c = new Creature("Grizzly Bears", "1G", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    private Land PlaceOnBattlefield()
    {
        var tomb = TombOfTheSpiritDragonFactory.Create(_alice);
        tomb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tomb);
        return tomb;
    }

    private static ActivatedAbility LifeAbility(Land tomb) =>
        tomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

    // -----------------------------------------------------------------------
    // Card shape (identity assert — Land, no subtypes/supertypes, colourless)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_Identity_IsColorlessLandWithNoSubtypesOrSupertypes()
    {
        var tomb = TombOfTheSpiritDragonFactory.Create(_alice);

        tomb.HasType(CardType.Land).Should().BeTrue();
        tomb.ManaCost.Should().BeNullOrEmpty(because: "Tomb of the Spirit Dragon has no mana cost");
        tomb.Subtypes.Should().BeEmpty();
        tomb.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        tomb.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Ability structure
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasManaAbilityPlusLifegainActivatedAbility()
    {
        var tomb = TombOfTheSpiritDragonFactory.Create(_alice);

        // {T}: Add {C} is a mana ability (from JSON).
        tomb.Abilities.OfType<IManaAbility>().Should().HaveCount(1,
            because: "{T}: Add {C} is the only mana ability");

        // The lifegain ability is a plain ActivatedAbility, NOT a mana ability.
        var life = LifeAbility(tomb);
        life.Should().NotBeAssignableTo<IManaAbility>(
            because: "gaining life is not a mana ability (CR 605.1a) — it uses the stack");

        // Costs: {2} generic + a tap-self cost.
        life.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(2,
            because: "the activation cost is {2}");
        life.Costs.Should().HaveCount(2, because: "{2} plus the {T} tap-self cost");
    }

    // -----------------------------------------------------------------------
    // First ability: {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbility_AddsOneColorless_TapsLand()
    {
        var tomb = PlaceOnBattlefield();

        var mana = tomb.Abilities.OfType<IManaAbility>().Single().Activate();

        // {C} is modelled as +1 generic.
        mana.Generic.Should().Be(1, because: "{T}: Add {C} produces one colourless pip");
        tomb.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // CountColorlessCreatures (CR 105.2a)
    // -----------------------------------------------------------------------

    [Fact]
    public void CountColorlessCreatures_CountsOnlyColorlessCreatures()
    {
        PlaceOnBattlefield(); // Tomb itself is a Land — never counts
        AddColorlessCreature(_alice, "Spirit A"); // counts
        AddColorlessCreature(_alice, "Spirit B"); // counts
        AddGreenCreature(_alice);                 // coloured → does NOT count

        // A colourless non-creature artifact does not count either.
        var artifact = new Artifact("Ornithopter Shell", "0");
        artifact.SetOwner(_alice); artifact.SetController(_alice);
        artifact.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(artifact);

        TombOfTheSpiritDragonFactory.CountColorlessCreatures(_alice).Should().Be(2,
            because: "only the two colourless creatures count — not the green creature, "
                   + "the colourless artifact, or the Tomb itself");
    }

    [Fact]
    public void CountColorlessCreatures_NullPlayer_IsZero()
    {
        TombOfTheSpiritDragonFactory.CountColorlessCreatures(null!).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Second ability: {2},{T}: gain 1 life per colourless creature
    // -----------------------------------------------------------------------

    [Fact]
    public void LifeAbility_GainsOneLifePerColorlessCreature()
    {
        var tomb = PlaceOnBattlefield();
        AddColorlessCreature(_alice, "Spirit A");
        AddColorlessCreature(_alice, "Spirit B");
        AddColorlessCreature(_alice, "Spirit C");
        AddGreenCreature(_alice); // does not contribute

        foreach (var e in LifeAbility(tomb).Effects) e.Execute();

        _alice.LifeTotal.Should().Be(23,
            because: "3 colourless creatures → gain 3 life (the green creature is ignored)");
    }

    [Fact]
    public void LifeAbility_ZeroColorlessCreatures_GainsNoLife()
    {
        var tomb = PlaceOnBattlefield();
        AddGreenCreature(_alice); // only a coloured creature present

        foreach (var e in LifeAbility(tomb).Effects) e.Execute();

        _alice.LifeTotal.Should().Be(20,
            because: "no colourless creatures → 0 life gained (legal activation, CR 608.2)");
    }
}
