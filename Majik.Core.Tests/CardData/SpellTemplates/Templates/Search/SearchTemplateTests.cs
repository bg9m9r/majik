using FluentAssertions;
using Majik.Core.CardData;
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

    [Theory]
    [InlineData("Search your library for a Demon card, reveal it, put it into your hand, then shuffle.")]
    [InlineData("Search your library for an Arcane card, reveal that card, put it into your hand, then shuffle.")]
    [InlineData("Search your library for a Mercenary card, reveal that card, put it into your hand, then shuffle.")]
    [InlineData("Search your library for a Vehicle card, reveal it, and put it into your hand. Then shuffle.")]
    [InlineData("Search your library for up to two Forest cards, reveal those cards, put them into your hand, then shuffle.")]
    public void KindedTutorTemplate_MatchesKindedTutorOracle(string oracle)
    {
        new KindedTutorTemplate().TryBind(Ctx(oracle)).Should().NotBeNull();
    }

    [Fact]
    public void KindedTutorTemplate_PriorityLowerThan_SearchLibraryTemplate()
    {
        new KindedTutorTemplate().Priority
            .Should().BeLessThan(new SearchLibraryTemplate().Priority);
    }

    [Fact]
    public void KindedTutorTemplate_DoesNotMatch_LandToBattlefield()
    {
        new KindedTutorTemplate().TryBind(
            Ctx("Search your library for a basic land card, put it onto the battlefield tapped, then shuffle."))
            .Should().BeNull();
    }

    [Theory]
    // Cultivate / Kodama's Reach — "put one onto the battlefield tapped and the
    // other into your hand". v1 binds the first land's tapped-ramp step; the
    // hand-bound second land is approximated away (see template comment).
    [InlineData("Search your library for up to two basic land cards, reveal those cards, put one onto the battlefield tapped and the other into your hand, then shuffle.")]
    public void SearchLandToBattlefieldTappedTemplate_MatchesCultivateShape(string oracle)
    {
        new SearchLandToBattlefieldTappedTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Fact]
    public void SearchLandToBattlefieldTappedTemplate_DoesNotMatch_PureTutorToHand()
    {
        // Regression guard — extending the put-subject to include "one"
        // must not pull in pure tutor-to-hand shapes.
        new SearchLandToBattlefieldTappedTemplate().TryBind(
            Ctx("Search your library for a basic land card, reveal it, put it into your hand, then shuffle."))
            .Should().BeNull();
    }
}
