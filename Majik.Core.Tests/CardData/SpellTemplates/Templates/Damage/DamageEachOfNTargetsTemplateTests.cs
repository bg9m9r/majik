using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

public class DamageEachOfNTargetsTemplateTests
{
    private static SpellBindContext Ctx(string name, string oracle)
    {
        var e = new CardEntity { Name = name, OracleText = oracle, TypeLine = "Instant" };
        return new SpellBindContext(e, new Player("X", 20), x => x, null, null);
    }

    [Theory]
    [InlineData("Furious Reprisal",     "Furious Reprisal deals 2 damage to each of two targets.")]
    [InlineData("Pinnacle of Rage",     "Pinnacle of Rage deals 3 damage to each of two targets.")]
    public void Binds_DealsNDamageToEachOfTwoTargets(string name, string oracle)
    {
        new DamageEachOfNTargetsTemplate().TryBind(Ctx(name, oracle)).Should().NotBeNull();
    }

    [Fact]
    public void RejectsXDamageVariant()
    {
        // "X damage to each of two targets" — value isn't literal; covered
        // by a different template (Fall of the Titans-style).
        new DamageEachOfNTargetsTemplate().TryBind(
            Ctx("Fall of the Titans",
                "Fall of the Titans deals X damage to each of up to two targets."))
            .Should().BeNull();
    }
}
