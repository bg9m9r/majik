using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class RevealNPutOneCreatureOntoBattlefieldTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void Matches_AethermagesTouch()
    {
        new RevealNPutOneCreatureOntoBattlefieldTemplate().TryBind(
            Ctx("Reveal the top five cards of your library. You may put a creature card from among them onto the battlefield. It gains \"At the beginning of your end step, return this creature to its owner's hand.\" Then put the rest of the cards revealed this way on the bottom of your library in any order."))
            .Should().NotBeNull();
    }

    [Fact]
    public void Matches_SeeTheUnwritten()
    {
        new RevealNPutOneCreatureOntoBattlefieldTemplate().TryBind(
            Ctx("Reveal the top five cards of your library. You may put a creature card from among them onto the battlefield. Then put the rest on the bottom of your library in a random order."))
            .Should().NotBeNull();
    }

    [Theory]
    // Should NOT match: hand-only destination (ImpulseMayRevealFilter shape — different effect tree).
    [InlineData("Reveal the top four cards of your library. You may put a creature card from among them into your hand. Put the rest on the bottom of your library in any order.")]
    [InlineData("Look at the top three cards of your library. You may reveal a creature card from among them and put it into your hand. Put the rest on the bottom of your library in any order.")]
    // Should NOT match: "any number" — that's the other template.
    [InlineData("Reveal the top six cards of your library. You may put any number of creature cards from among them onto the battlefield. Put the rest on the bottom of your library in a random order.")]
    // Should NOT match: until-you-reveal walk (RevealUntilLand/Artifact templates).
    [InlineData("Reveal cards from the top of your library until you reveal a land card. Put that card onto the battlefield and the rest on the bottom of your library in any order.")]
    public void DoesNotMatch_OutOfFamily(string oracle)
    {
        new RevealNPutOneCreatureOntoBattlefieldTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }
}
