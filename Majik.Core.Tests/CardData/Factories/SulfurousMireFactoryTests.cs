using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SulfurousMireFactory"/> — the B/R snow dual land.
/// Oracle text:
///   "({T}: Add {B} or {R}.)
///    This land enters tapped."
///
/// Type line: Snow Land — Swamp Mountain.
///
/// Covers:
/// - Identity: Land type, Snow supertype (CR 205.4d), Swamp + Mountain
///   land subtypes (CR 205.3i).
/// - Two mana abilities producing {B} and {R} respectively (CR 605.1 —
///   mana abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by
/// this factory (same posture as the Ice Tunnel / Guildgate factories).
/// </summary>
public class SulfurousMireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SulfurousMire_Dispatch_ReturnsLandWithCorrectName()
    {
        var card = NamedCardFactory.Create("Sulfurous Mire", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Sulfurous Mire");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SulfurousMire_HasSnowSupertype()
    {
        var land = (Land)NamedCardFactory.Create("Sulfurous Mire", _alice);

        land.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Sulfurous Mire is a Snow Land (CR 205.4d)");
    }

    [Fact]
    public void SulfurousMire_HasSwampAndMountainSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Sulfurous Mire", _alice);

        land.HasSubtype(CardSubtype.Swamp).Should().BeTrue(
            "Sulfurous Mire is a Swamp land (CR 205.3i)");
        land.HasSubtype(CardSubtype.Mountain).Should().BeTrue(
            "Sulfurous Mire is a Mountain land (CR 205.3i)");
    }

    [Fact]
    public void SulfurousMire_HasTwoManaAbilities_ProducingBlackAndRed()
    {
        var land = (Land)NamedCardFactory.Create("Sulfurous Mire", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Sulfurous Mire taps for {B} or {R}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void SulfurousMire_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Sulfurous Mire", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
}
