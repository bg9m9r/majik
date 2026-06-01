using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PrairieStreamFactory"/> — Battle for Zendikar
/// W/U "battle land" (a.k.a. tango land).
///
/// Oracle (Scryfall, verified):
///   "({T}: Add {W} or {U}.)
///    This land enters tapped unless you control two or more basic lands."
/// Type line: "Land — Plains Island".
///
/// Covers card identity (Land type, printed name, owner/controller wiring,
/// non-Basic / non-Legendary), the printed Plains + Island subtypes, and
/// the two mana abilities ({W} + {U}). The conditional ETB-tapped
/// ("two or more basic lands", CR 614.1c) is a replacement effect handled
/// by the binder layer in the production load path, not by this thin
/// JSON-backed factory — same posture as the Kaladesh fastland
/// (<see cref="BloomingMarshFactory"/>) and M10/Innistrad check-land
/// factories.
/// </summary>
public class PrairieStreamTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PrairieStream_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void PrairieStream_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.Name.Should().Be("Prairie Stream");
    }

    [Fact]
    public void PrairieStream_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PrairieStream_HasPrintedPlainsAndIslandSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        // Type line "Land — Plains Island": both basic-land subtypes are
        // printed on this battle land (unlike the M10/Innistrad check lands,
        // which carry no printed subtype).
        land.HasSubtype(CardSubtype.Plains).Should().BeTrue();
        land.HasSubtype(CardSubtype.Island).Should().BeTrue();
    }

    [Fact]
    public void PrairieStream_IsNotBasic_NotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "battle lands are nonbasic despite their Plains/Island subtypes");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void PrairieStream_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void PrairieStream_HasWhiteManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void PrairieStream_HasBlueManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void PrairieStream_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-two-or-more-basics is a replacement effect, not a trigger");
    }

    [Fact]
    public void PrairieStream_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Prairie Stream", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void PrairieStream_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Prairie Stream", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Prairie Stream");
    }
}
