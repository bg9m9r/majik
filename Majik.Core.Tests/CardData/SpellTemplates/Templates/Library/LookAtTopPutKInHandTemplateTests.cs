using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Library;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Library;

public class LookAtTopPutKInHandTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Theory]
    [InlineData("Look at the top four cards of your library. Put two of them into your hand and the rest into your graveyard.")]
    [InlineData("Look at the top four cards of your library. Put two of them into your hand and the rest on the bottom of your library in any order.")]
    [InlineData("Look at the top four cards of your library. Put two of them into your hand and the rest into your graveyard. You lose 2 life.")]
    public void Matches_LookNPutKFamily(string oracle)
    {
        new LookAtTopPutKInHandTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Single-keep is handled by LookAtTopPutOneInHandTemplate.
    [InlineData("Look at the top three cards of your library. Put one of them into your hand and the rest on the bottom of your library.")]
    public void DoesNotMatch_OutOfFamily(string oracle)
    {
        new LookAtTopPutKInHandTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }
}
