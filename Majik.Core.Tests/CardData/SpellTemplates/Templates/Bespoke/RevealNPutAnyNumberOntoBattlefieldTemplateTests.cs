using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class RevealNPutAnyNumberOntoBattlefieldTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void Matches_GenesisWave()
    {
        new RevealNPutAnyNumberOntoBattlefieldTemplate().TryBind(
            Ctx("Reveal the top X cards of your library. You may put any number of permanent cards with mana value X or less from among them onto the battlefield. Then put all cards revealed this way that weren't put onto the battlefield on the bottom of your library in a random order."))
            .Should().NotBeNull();
    }

    [Fact]
    public void Matches_GlacialRevelation()
    {
        new RevealNPutAnyNumberOntoBattlefieldTemplate().TryBind(
            Ctx("Reveal the top six cards of your library. You may put any number of snow permanent cards from among them onto the battlefield. Put the rest into your hand."))
            .Should().NotBeNull();
    }

    [Theory]
    // Should NOT match: "a creature card" — that's the singular template.
    [InlineData("Reveal the top five cards of your library. You may put a creature card from among them onto the battlefield. Then put the rest on the bottom of your library in a random order.")]
    // Should NOT match: into-hand-only destination (ImpulseMayRevealFilter shape).
    [InlineData("Reveal the top four cards of your library. You may put a creature card from among them into your hand. Put the rest on the bottom of your library in any order.")]
    // Should NOT match: until-you-reveal walk.
    [InlineData("Reveal cards from the top of your library until you reveal a land card. Put that card onto the battlefield and the rest on the bottom of your library in any order.")]
    public void DoesNotMatch_OutOfFamily(string oracle)
    {
        new RevealNPutAnyNumberOntoBattlefieldTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }
}
