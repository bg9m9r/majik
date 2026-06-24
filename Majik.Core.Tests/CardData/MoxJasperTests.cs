using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MoxJasperFactory"/>.
///
/// Mox Jasper — Legendary Artifact {0}.
/// "{T}: Add one mana of any color. Activate only if you control a Dragon."
///
/// Covers the card's UNIQUE behaviour (a Dragon-gated five-colour mana
/// rock) plus a single identity assert. Dispatch + well-formedness are
/// covered for every implemented card by CardFactoryContractTests.
///
/// - Identity: Legendary Artifact, mana cost {0}.
/// - Five mana abilities (one per WUBRG) — "any color".
/// - Inactive when no Dragon is controlled.
/// - Active (all five colours) when the controller controls a Dragon
///   (by printed subtype OR an artifact creature that is a Dragon).
/// - Opponent Dragons do not count (CR 605.1 / 602.5 — "you control").
/// - Non-Dragon creature does not count.
/// - Tap gate after activation.
/// </summary>
[Trait("Color", "C")]
public class MoxJasperTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // --------------------------------------------------------------
    // Identity
    // --------------------------------------------------------------

    [Fact]
    public void MoxJasper_IsLegendaryArtifact_ZeroCost()
    {
        var mox = MoxJasperFactory.Create(_alice);

        mox.Name.Should().Be("Mox Jasper");
        mox.HasType(CardType.Artifact).Should().BeTrue("Mox Jasper is an Artifact");
        mox.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Mox Jasper is Legendary");
        mox.ManaCost.Should().Be("{0}");
        mox.Owner.Should().BeSameAs(_alice);
        mox.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MoxJasper_HasFiveManaAbilities_OnePerColor()
    {
        var mox = MoxJasperFactory.Create(_alice);
        var mas = mox.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour — 'any color'");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.TotalValue == 1);
    }

    // --------------------------------------------------------------
    // Gate — off
    // --------------------------------------------------------------

    [Fact]
    public void MoxJasper_CannotActivate_WithNoDragon()
    {
        var mox = MoxJasperFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "no Dragon on the controller's battlefield — Mox Jasper stays gated");
        }
    }

    [Fact]
    public void MoxJasper_NonDragonCreature_DoesNotCount()
    {
        var mox = MoxJasperFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        // A creature with no Dragon subtype.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);

        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "the creature is not a Dragon — Mox Jasper stays gated");
        }
    }

    // --------------------------------------------------------------
    // Gate — on
    // --------------------------------------------------------------

    [Fact]
    public void MoxJasper_ControlledDragon_UnlocksAllFiveColors()
    {
        var mox = MoxJasperFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        var dragon = new Creature(
            "Shivan Dragon",
            "{4}{R}{R}",
            5, 5,
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(_alice);
        dragon.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(dragon);

        var mas = mox.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue(
                "controlling a Dragon unlocks 'add one mana of any color' (all five colours)");
        }

        // Activate one and verify mana production + the tap gate.
        var red = mas.Single(m => m.ManaGenerated.Red == 1);
        var produced = red.Activate();
        produced.Red.Should().Be(1);
        produced.TotalValue.Should().Be(1);
        mox.IsTapped.Should().BeTrue("activating taps Mox Jasper");

        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse("Mox Jasper is tapped");
        }
    }

    // --------------------------------------------------------------
    // "you control" — opponent Dragons don't count
    // --------------------------------------------------------------

    [Fact]
    public void MoxJasper_OpponentsDragonsDoNotCount()
    {
        var mox = MoxJasperFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        var bobsDragon = new Creature(
            "Bob's Dragon",
            "{4}{R}{R}",
            5, 5,
            subtypes: new[] { CardSubtype.Dragon });
        bobsDragon.SetOwner(_bob);
        bobsDragon.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobsDragon);

        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "opponent Dragons do not count toward Alice's Mox Jasper");
        }
    }
}
