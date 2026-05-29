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
/// Unit tests for <see cref="AlpineMeadowFactory"/> — the R/W Kaldheim snow
/// dual land. Type line: <c>Snow Land — Mountain Plains</c>. Oracle text:
///   "({T}: Add {R} or {W}.)
///    This land enters tapped."
///
/// Covers:
/// - Identity (Land + Snow supertype, CR 205.4d, + the printed Mountain and
///   Plains subtypes, CR 205.3i).
/// - Two mana abilities producing {R} and {W} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Guildgate factories).
/// </summary>
public class AlpineMeadowFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AlpineMeadow_Dispatch_ReturnsSnowLandWithMountainAndPlainsSubtypes()
    {
        var card = NamedCardFactory.Create("Alpine Meadow", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Alpine Meadow");
        card.HasSupertype(CardSupertype.Snow).Should().BeTrue("type line is Snow Land");
        card.HasSubtype(CardSubtype.Mountain).Should().BeTrue();
        card.HasSubtype(CardSubtype.Plains).Should().BeTrue();
    }

    [Fact]
    public void AlpineMeadow_HasTwoManaAbilities_ProducingRedAndWhite()
    {
        var land = (Land)NamedCardFactory.Create("Alpine Meadow", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Alpine Meadow taps for {R} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void AlpineMeadow_HasNoNonManaActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Alpine Meadow", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("the snow dual lands have no activated abilities");
    }
}
