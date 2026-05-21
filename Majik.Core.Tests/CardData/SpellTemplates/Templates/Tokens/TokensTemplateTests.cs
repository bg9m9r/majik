using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Tokens;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Tokens;

public class TokensTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void InvestigateNTimesTemplate_MatchesInvestigateTwoTimes()
    {
        new InvestigateNTimesTemplate()
            .TryBind(Ctx("Investigate two times."))
            .Should().NotBeNull();
    }

    [Fact]
    public void InvestigateSingleTemplate_MatchesInvestigate()
    {
        new InvestigateSingleTemplate()
            .TryBind(Ctx("Investigate."))
            .Should().NotBeNull();
    }

    [Fact]
    public void CreateTreasureTokensTemplate_MatchesCreateATreasureToken()
    {
        new CreateTreasureTokensTemplate()
            .TryBind(Ctx("Create a Treasure token."))
            .Should().NotBeNull();
    }

    [Fact]
    public void CreateFoodTokensTemplate_MatchesCreateTwoFoodTokens()
    {
        new CreateFoodTokensTemplate()
            .TryBind(Ctx("Create two Food tokens."))
            .Should().NotBeNull();
    }

    [Fact]
    public void CreateClueTokensTemplate_MatchesCreateAClueToken()
    {
        new CreateClueTokensTemplate()
            .TryBind(Ctx("Create a Clue token."))
            .Should().NotBeNull();
    }

    [Fact]
    public void CreateTokensTemplate_MatchesCreateCreatureToken()
    {
        new CreateTokensTemplate()
            .TryBind(Ctx("Create a 2/2 green Wolf creature token with trample."))
            .Should().NotBeNull();
    }

    [Fact]
    public void InvestigateNTimes_Priority_Beats_InvestigateSingle()
    {
        new InvestigateNTimesTemplate().Priority
            .Should().BeGreaterThan(new InvestigateSingleTemplate().Priority);
    }
}
