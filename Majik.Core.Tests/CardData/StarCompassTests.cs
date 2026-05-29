using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="StarCompassFactory"/>.
///
/// Star Compass — Artifact {2}.
///   "This artifact enters tapped.
///    {T}: Add one mana of any color that a basic land you control could
///    produce."
///
/// Covers:
/// - Card identity (Artifact, mana cost {2}).
/// - NamedCardFactory dispatch.
/// - Per-colour mana-ability shape (five WUBRG slots; CR 305.6 maps basic
///   subtypes → colours).
/// - Conditional activation: a colour slot is only active while the
///   controller controls a basic land of the matching subtype.
/// </summary>
public class StarCompassTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void StarCompass_IsArtifact_TwoCost()
    {
        var compass = StarCompassFactory.Create(_alice);

        compass.Name.Should().Be("Star Compass");
        compass.HasType(CardType.Artifact).Should().BeTrue();
        compass.ManaCost.Should().Be("{2}");
        compass.Owner.Should().BeSameAs(_alice);
        compass.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StarCompass()
    {
        var card = NamedCardFactory.Create("Star Compass", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Star Compass");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
    }

    // --------------------------------------------------------------
    // Ability shape — five colour-specific mana abilities (WUBRG).
    // --------------------------------------------------------------

    [Fact]
    public void StarCompass_HasFiveManaAbilities_OnePerColor()
    {
        var compass = StarCompassFactory.Create(_alice);

        var manaAbilities = compass.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5);

        // Each produces exactly one coloured pip.
        manaAbilities.Should().OnlyContain(ma => ma.ManaGenerated.TotalValue == 1);
    }

    // --------------------------------------------------------------
    // Conditional activation — CR 305.6 / "could produce".
    // --------------------------------------------------------------

    [Fact]
    public void NoBasicLands_NoColorIsActivatable()
    {
        var compass = StarCompassFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(compass);
        compass.SetZone(ZoneType.Battlefield);

        compass.Abilities.OfType<ManaAbility>()
            .Should().OnlyContain(ma => ma.CanActivate() == false,
                "no basic land you control can produce any colour");
    }

    [Fact]
    public void ControlAForest_OnlyGreenIsActivatable()
    {
        var compass = StarCompassFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(compass);
        compass.SetZone(ZoneType.Battlefield);

        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var green = StarCompassFactory.AbilityForColor(compass, "G");
        green.CanActivate().Should().BeTrue("a Forest you control produces green");

        foreach (var pip in new[] { "W", "U", "B", "R" })
        {
            StarCompassFactory.AbilityForColor(compass, pip)
                .CanActivate().Should().BeFalse(
                    $"no basic land you control produces {pip}");
        }
    }

    [Fact]
    public void TappedCompass_CannotActivate_EvenWithMatchingBasic()
    {
        var compass = StarCompassFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(compass);
        compass.SetZone(ZoneType.Battlefield);
        compass.Tap();

        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);
        island.SetZone(ZoneType.Battlefield);

        StarCompassFactory.AbilityForColor(compass, "U")
            .CanActivate().Should().BeFalse("Star Compass is tapped");
    }

    [Fact]
    public void ActivateGreen_AddsGreenMana()
    {
        var compass = StarCompassFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(compass);
        compass.SetZone(ZoneType.Battlefield);

        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var green = StarCompassFactory.AbilityForColor(compass, "G");
        var produced = green.Activate();

        produced.TotalValue.Should().Be(1);
        compass.IsTapped.Should().BeTrue("activating taps Star Compass (CR 605.1)");
    }
}
