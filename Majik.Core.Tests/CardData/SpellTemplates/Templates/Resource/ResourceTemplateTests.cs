using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Resource;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Resource;

public class ResourceTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void DrawCardsTemplate_MatchesDrawThreeCards()
    {
        new DrawCardsTemplate().TryBind(Ctx("Draw three cards."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DiscardTemplate_MatchesTargetPlayerDiscardsTwo()
    {
        new DiscardTemplate().TryBind(Ctx("Target player discards two cards."))
            .Should().NotBeNull();
    }

    [Fact]
    public void GainLifeTemplate_MatchesTargetPlayerGainsFive()
    {
        new GainLifeTemplate().TryBind(Ctx("Target player gains 5 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void YouGainLifeTemplate_MatchesYouGainThreeLife()
    {
        new YouGainLifeTemplate().TryBind(Ctx("You gain 3 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void YouLoseLifeTemplate_MatchesYouLoseTwoLife()
    {
        new YouLoseLifeTemplate().TryBind(Ctx("You lose 2 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void EachPlayerDrawsTemplate_MatchesEachPlayerDrawsACard()
    {
        new EachPlayerDrawsTemplate().TryBind(Ctx("Each player draws a card."))
            .Should().NotBeNull();
    }

    [Fact]
    public void TargetPlayerLosesLifeTemplate_MatchesTargetPlayerLosesFourLife()
    {
        new TargetPlayerLosesLifeTemplate().TryBind(Ctx("Target player loses 4 life."))
            .Should().NotBeNull();
    }
}
