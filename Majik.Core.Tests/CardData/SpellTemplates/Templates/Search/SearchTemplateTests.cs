using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Search;

public class SearchTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void SearchLandToBattlefieldTappedTemplate_MatchesTappedLandSearch()
    {
        new SearchLandToBattlefieldTappedTemplate().TryBind(
            Ctx("Search your library for a basic land card, put it onto the battlefield tapped, then shuffle."))
            .Should().NotBeNull();
    }

    [Fact]
    public void SearchLandToBattlefieldTemplate_MatchesUntappedLandSearch()
    {
        new SearchLandToBattlefieldTemplate().TryBind(
            Ctx("Search your library for a basic land card and put it onto the battlefield."))
            .Should().NotBeNull();
    }

    [Fact]
    public void GreenSunsZenithPatternTemplate_MatchesGreenSunsZenithOracle()
    {
        new GreenSunsZenithPatternTemplate().TryBind(
            Ctx("Search your library for a green creature card with mana value X or less, put it onto the battlefield, then shuffle. Shuffle Green Sun's Zenith into its owner's library."))
            .Should().NotBeNull();
    }

    [Fact]
    public void SearchLibraryTemplate_MatchesGenericCreatureSearch()
    {
        new SearchLibraryTemplate().TryBind(
            Ctx("Search your library for a creature card."))
            .Should().NotBeNull();
    }

    [Fact]
    public void TappedTemplate_PriorityHigherThan_UntappedTemplate()
    {
        new SearchLandToBattlefieldTappedTemplate().Priority
            .Should().BeGreaterThan(new SearchLandToBattlefieldTemplate().Priority);
    }

    [Fact]
    public void GreenSunsZenithTemplate_PriorityHigherThan_SearchLibraryTemplate()
    {
        new GreenSunsZenithPatternTemplate().Priority
            .Should().BeGreaterThan(new SearchLibraryTemplate().Priority);
    }
}
