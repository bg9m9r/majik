using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

public class ChooseAndFightTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Theory]
    [InlineData("Choose target creature you control and target creature you don't control. Then those creatures fight each other.")]
    [InlineData("Choose target creature you control and target creature you don't control. If you control three or more snow permanents, the creature you control gets +1/+0 and gains indestructible until end of turn. Then those creatures fight each other.")]
    [InlineData("Choose target creature you control and target creature you don't control. The creature you control gets +2/+1 until end of turn if it's a Knight. Then those creatures fight each other.")]
    [InlineData("Choose target creature you control and target creature you don't control. If the creature you control entered this turn, put a +1/+1 counter on it. Then those creatures fight each other.")]
    public void Matches_ChooseAndFightFamily(string oracle)
    {
        new ChooseAndFightTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Inline FightTemplate shape — different template owns it.
    [InlineData("Target creature you control fights target creature you don't control.")]
    // Asymmetric — no fight back.
    [InlineData("Target creature you control deals damage equal to its power to target creature you don't control.")]
    public void DoesNotMatch_OutOfFamily(string oracle)
    {
        new ChooseAndFightTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }
}
