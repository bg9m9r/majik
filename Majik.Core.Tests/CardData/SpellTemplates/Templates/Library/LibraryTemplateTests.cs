using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Library;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Library;

public class LibraryTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void MillTargetTemplate_MatchesTargetPlayerMillsThreeCards()
    {
        new MillTargetTemplate().TryBind(Ctx("Target player mills 3 cards."))
            .Should().NotBeNull();
    }

    [Fact]
    public void MillSelfTemplate_MatchesMillTwoCards()
    {
        new MillSelfTemplate().TryBind(Ctx("Mill 2 cards."))
            .Should().NotBeNull();
    }

    [Fact]
    public void EachOpponentMillsTemplate_MatchesEachOpponentMillsFiveCards()
    {
        new EachOpponentMillsTemplate().TryBind(Ctx("Each opponent mills 5 cards."))
            .Should().NotBeNull();
    }

    [Fact]
    public void EachPlayerMillsTemplate_MatchesEachPlayerMillsTwoCards()
    {
        new EachPlayerMillsTemplate().TryBind(Ctx("Each player mills 2 cards."))
            .Should().NotBeNull();
    }

    [Fact]
    public void SurveilSelfTemplate_MatchesSurveilTwo()
    {
        new SurveilSelfTemplate().TryBind(Ctx("Surveil 2."))
            .Should().NotBeNull();
    }

    [Fact]
    public void ScrySelfTemplate_MatchesStandaloneScryOne()
    {
        new ScrySelfTemplate().TryBind(Ctx("Scry 1."))
            .Should().NotBeNull();
    }

    [Fact]
    public void ScryNTemplate_MatchesScryInCantrip()
    {
        // "Scry 1, then draw a card." — the standalone anchor fails on this text
        // because the comma and draw clause follow scry on the same line, preventing
        // the ScrySelf multiline anchor from matching cleanly. ScryN (non-anchored)
        // catches these cantrip cases.
        new ScryNTemplate().TryBind(Ctx("Scry 1, then draw a card."))
            .Should().NotBeNull();
    }

    [Fact]
    public void ReanimateFromGraveyardTemplate_MatchesReturnCreatureCard()
    {
        new ReanimateFromGraveyardTemplate().TryBind(
            Ctx("Return target creature card from your graveyard to your hand."))
            .Should().NotBeNull();
    }

    [Fact]
    public void ExileFromGraveyardTemplate_MatchesExileTargetCardFromGraveyard()
    {
        new ExileFromGraveyardTemplate().TryBind(
            Ctx("Exile target card from a graveyard."))
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData("Return all enchantment cards from your graveyard to the battlefield.")]
    [InlineData("Return all artifact and enchantment cards from your graveyard to the battlefield.")]
    [InlineData("Return all artifact, enchantment, and planeswalker cards from your graveyard to the battlefield.")]
    [InlineData("Return all land cards from your graveyard to the battlefield tapped.")]
    [InlineData("Return all creature cards from your graveyard to the battlefield.")]
    public void ReturnAllFromGraveyardTemplate_MatchesMassReanimation(string oracle)
    {
        new ReturnAllFromGraveyardTemplate().TryBind(Ctx(oracle)).Should().NotBeNull();
    }

    [Fact]
    public void ReturnAllFromGraveyardTemplate_DoesNotMatch_TargetSingleReanimation()
    {
        new ReturnAllFromGraveyardTemplate().TryBind(
            Ctx("Return target creature card from your graveyard to the battlefield."))
            .Should().BeNull();
    }

    // ImpulseMayRevealFilterTemplate — dual-filter "and/or" variants (no space
    // around the slash). Cards: In the Presence of Ages, Relentless Pursuit,
    // Benefaction of Rhonas. Before the broaden the regex required either "/"
    // (with no "or") or "\s+or", so "and/or" — slash glued to "or" — failed.
    [Theory]
    [InlineData("Reveal the top four cards of your library. You may put a creature card and/or a land card from among them into your hand. Put the rest into your graveyard.")]
    [InlineData("Reveal the top five cards of your library. You may put a creature card and/or an enchantment card from among them into your hand. Put the rest into your graveyard.")]
    public void ImpulseMayRevealFilterTemplate_MatchesAndOrDualFilter(string oracle)
    {
        new ImpulseMayRevealFilterTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }
}
