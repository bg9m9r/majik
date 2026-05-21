using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class BespokeTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void ThoughtseizePatternTemplate_MatchesThoughtseizeOracle()
    {
        new ThoughtseizePatternTemplate().TryBind(
            Ctx("Target player reveals their hand. You choose a nonland card from it. That player discards that card. You lose 2 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void MalevolentRumblePatternTemplate_MatchesMalevolentRumbleOracle()
    {
        new MalevolentRumblePatternTemplate().TryBind(
            Ctx("Reveal the top four cards of your library. You may put a permanent card from among them into your hand. Put the rest into your graveyard. Create a 0/1 colorless Eldrazi Spawn creature token with \"Sacrifice this creature: Add {C}.\""))
            .Should().NotBeNull();
    }
}
