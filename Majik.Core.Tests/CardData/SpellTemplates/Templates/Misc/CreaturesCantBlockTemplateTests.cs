using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Misc;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Misc;

public class CreaturesCantBlockTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Theory]
    [InlineData("Creatures can't block this turn.")]
    [InlineData("Creatures without flying can't block this turn.")]
    [InlineData("Nonartifact creatures can't block this turn.")]
    [InlineData("Monocolored creatures can't block this turn.")]
    [InlineData("Green creatures and white creatures can't block this turn.")]
    public void Matches_SingleClauseLockouts(string oracle)
    {
        new CreaturesCantBlockTemplate().TryBind(Ctx(oracle)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("Target creature can't block this turn.")]
    [InlineData("X target creatures can't block this turn.")]
    [InlineData("Up to two target creatures can't block this turn.")]
    public void DoesNotMatch_TargetedVariants(string oracle)
    {
        new CreaturesCantBlockTemplate().TryBind(Ctx(oracle)).Should().BeNull();
    }
}
