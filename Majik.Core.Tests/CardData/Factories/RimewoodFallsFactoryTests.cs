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
/// Unit tests for <see cref="RimewoodFallsFactory"/> — the G/U Kaldheim snow
/// dual land. Type line: <c>Snow Land — Forest Island</c>. Oracle text:
///   "({T}: Add {G} or {U}.)
///    This land enters tapped."
///
/// Covers:
/// - Identity (Land + Snow supertype, CR 205.4d, + the printed Forest and
///   Island subtypes, CR 205.3i).
/// - Two mana abilities producing {G} and {U} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the sibling snow duals).
/// </summary>
public class RimewoodFallsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RimewoodFalls_Dispatch_ReturnsSnowLandWithForestAndIslandSubtypes()
    {
        var card = NamedCardFactory.Create("Rimewood Falls", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Rimewood Falls");
        card.HasSupertype(CardSupertype.Snow).Should().BeTrue("type line is Snow Land");
        card.HasSubtype(CardSubtype.Forest).Should().BeTrue();
        card.HasSubtype(CardSubtype.Island).Should().BeTrue();
    }

    [Fact]
    public void RimewoodFalls_HasTwoManaAbilities_ProducingGreenAndBlue()
    {
        var land = (Land)NamedCardFactory.Create("Rimewood Falls", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Rimewood Falls taps for {G} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void RimewoodFalls_HasNoNonManaActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Rimewood Falls", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("the snow dual lands have no activated abilities");
    }
}
