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
/// Unit tests for <see cref="ArcticTreelineFactory"/> — the G/W snow dual land
/// from the Kaldheim "snow taplands" cycle. Oracle text:
///   "This land enters tapped.
///    {T}: Add {G} or {W}."
///
/// Covers:
/// - Identity: Land type, Snow supertype (CR 205.4d), Forest + Plains land
///   subtypes (CR 205.3i).
/// - Two mana abilities producing {G} and {W} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Guildgate factories).
/// </summary>
public class ArcticTreelineFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ArcticTreeline_Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Arctic Treeline", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Arctic Treeline");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ArcticTreeline_HasSnowSupertype()
    {
        var land = (Land)NamedCardFactory.Create("Arctic Treeline", _alice);

        land.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Arctic Treeline is a Snow Land (CR 205.4d)");
    }

    [Fact]
    public void ArcticTreeline_HasForestAndPlainsSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Arctic Treeline", _alice);

        land.HasSubtype(CardSubtype.Forest).Should().BeTrue(
            "Arctic Treeline is a Forest land (CR 205.3i)");
        land.HasSubtype(CardSubtype.Plains).Should().BeTrue(
            "Arctic Treeline is a Plains land (CR 205.3i)");
    }

    [Fact]
    public void ArcticTreeline_HasTwoManaAbilities_ProducingGreenAndWhite()
    {
        var land = (Land)NamedCardFactory.Create("Arctic Treeline", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Arctic Treeline taps for {G} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void ArcticTreeline_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Arctic Treeline", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
}
