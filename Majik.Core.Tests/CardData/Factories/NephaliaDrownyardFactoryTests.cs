using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="NephaliaDrownyardFactory"/> (Innistrad and reprints).
/// Land:
///   "{T}: Add {C}.
///    {1}{U}{B}, {T}: Target player mills three cards."
///
/// Covers:
/// - Identity (Land, no supertype/subtype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} mana ability from JSON.
/// - {1}{U}{B},{T} activated ability shape (ManaCostCost {1}{U}{B} + tap cost,
///   one "target player" TargetRequest, instant speed).
/// - Mill three from the chosen target player (CR 701.13).
/// - Short library fully mills without losing the game (CR 701.13a).
/// - No-op when the chosen target isn't a Player (CR 608.2b).
/// </summary>
[Trait("Color", "C")]
public class NephaliaDrownyardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NephaliaDrownyard_Identity()
    {
        var land = NephaliaDrownyardFactory.Create(_alice);

        land.Name.Should().Be("Nephalia Drownyard");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Nephalia Drownyard is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NephaliaDrownyard_Dispatch_ViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Nephalia Drownyard", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Nephalia Drownyard");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} mana ability (from JSON)
    // -----------------------------------------------------------------------

    [Fact]
    public void NephaliaDrownyard_HasColorlessManaAbility()
    {
        var land = NephaliaDrownyardFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().ContainSingle(
            "the only mana ability is {T}: Add {C}");
    }

    // -----------------------------------------------------------------------
    // {1}{U}{B}, {T}: Target player mills three cards — ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void NephaliaDrownyard_MillAbility_HasCostUB1AndTapAndPlayerTarget()
    {
        var land = NephaliaDrownyardFactory.Create(_alice);

        var mill = land.Abilities.OfType<ActivatedAbility>().Single();

        mill.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the mill cost has one ManaCostCost ({1}{U}{B})");
        mill.Costs.Count(c => c is not ManaCostCost).Should().BeGreaterThan(0,
            "the {T} tap component is an additional (non-mana) cost");
        mill.IsSorcerySpeed.Should().BeFalse(
            "the mill ability is instant-speed per oracle");
        mill.TargetRequests.Should().ContainSingle();
        mill.TargetRequests[0].Description.Should().Be("target player");
        mill.TargetRequests[0].MinTargets.Should().Be(1);
        mill.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve — mill three (CR 701.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void NephaliaDrownyard_MillAbility_MillsThreeFromChosenPlayer()
    {
        // Seed Bob's library with 6 cards so a mill-3 leaves 3.
        for (var i = 0; i < 6; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = NephaliaDrownyardFactory.Create(_alice);
        var mill = land.Abilities.OfType<ActivatedAbility>().Single();

        mill.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in mill.Effects) e.Execute();

        _bob.Zones.Library.GetCards().Should().HaveCount(3,
            "three of the six library cards were milled (CR 701.13)");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3,
            "the three milled cards are now in the graveyard");
    }

    [Fact]
    public void NephaliaDrownyard_MillAbility_ShortLibrary_MillsAllRemaining()
    {
        // Only 2 cards — fewer than MillCount=3.
        for (var i = 0; i < 2; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = NephaliaDrownyardFactory.Create(_alice);
        var mill = land.Abilities.OfType<ActivatedAbility>().Single();

        mill.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        var act = () => { foreach (var e in mill.Effects) e.Execute(); };

        act.Should().NotThrow(
            "CR 701.13a — milling more than the library has just mills all remaining");
        _bob.Zones.Library.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void NephaliaDrownyard_MillAbility_NoOps_WhenChosenTargetNotPlayer()
    {
        var land = NephaliaDrownyardFactory.Create(_alice);
        var mill = land.Abilities.OfType<ActivatedAbility>().Single();

        // A non-Player token chosen (illegal at resolution, CR 608.2b).
        mill.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { land },
        });

        var act = () => { foreach (var e in mill.Effects) e.Execute(); };

        act.Should().NotThrow("an illegal/non-Player target makes the ability no-op");
    }
}
