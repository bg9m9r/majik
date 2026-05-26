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
/// Tests for Darksteel Citadel (Darksteel).
///
/// Covers:
///   - Artifact + Land typing (multi-type, no Basic supertype) +
///     <see cref="NamedCardFactory"/> dispatch shape.
///   - Printed Indestructible keyword on the Citadel.
///   - {T}: Add {C} mana ability taps the land and produces colourless.
/// </summary>
public class DarksteelCitadelTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DarksteelCitadel_Identity_ArtifactLand()
    {
        var citadel = DarksteelCitadelFactory.Create(_alice);

        citadel.Name.Should().Be("Darksteel Citadel");
        citadel.HasType(CardType.Land).Should().BeTrue();
        citadel.HasType(CardType.Artifact).Should().BeTrue();
        citadel.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        citadel.Owner.Should().BeSameAs(_alice);
        citadel.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DarksteelCitadel_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Darksteel Citadel", _alice);

        card.Should().BeOfType<Land>();
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void DarksteelCitadel_HasPrintedIndestructibleKeyword()
    {
        var citadel = DarksteelCitadelFactory.Create(_alice);

        citadel.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void DarksteelCitadel_HasColorlessManaAbility_ActivationTapsLandAndProducesC()
    {
        var citadel = DarksteelCitadelFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(citadel);
        citadel.SetZone(ZoneType.Battlefield);

        var manaAbility = citadel.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} parses into the Generic slot (mirrors Wasteland's tap-for-{C}
        // and Phyrexian Tower's colourless test).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        citadel.IsTapped.Should().BeTrue();
    }
}
