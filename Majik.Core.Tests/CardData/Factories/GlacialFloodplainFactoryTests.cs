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
/// Unit tests for <see cref="GlacialFloodplainFactory"/> — the W/U Kaldheim snow
/// dual land. Type line: <c>Snow Land — Plains Island</c>. Oracle text:
///   "({T}: Add {W} or {U}.)
///    This land enters tapped."
///
/// Covers:
/// - Identity (Land + Snow supertype, CR 205.4d, + the printed Plains and
///   Island subtypes, CR 205.3i).
/// - Two mana abilities producing {W} and {U} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load path
/// by <see cref="EntersTappedBinder"/> from the oracle text, not by this factory
/// (same posture as <see cref="AlpineMeadowFactory"/>).
/// </summary>
public class GlacialFloodplainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GlacialFloodplain_Dispatch_ReturnsSnowLandWithPlainsAndIslandSubtypes()
    {
        var card = NamedCardFactory.Create("Glacial Floodplain", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Glacial Floodplain");
        card.HasSupertype(CardSupertype.Snow).Should().BeTrue("type line is Snow Land");
        card.HasSubtype(CardSubtype.Plains).Should().BeTrue();
        card.HasSubtype(CardSubtype.Island).Should().BeTrue();
    }

    [Fact]
    public void GlacialFloodplain_HasTwoManaAbilities_ProducingWhiteAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Glacial Floodplain", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Glacial Floodplain taps for {W} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void GlacialFloodplain_HasNoNonManaActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Glacial Floodplain", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("the snow dual lands have no activated abilities");
    }
}
