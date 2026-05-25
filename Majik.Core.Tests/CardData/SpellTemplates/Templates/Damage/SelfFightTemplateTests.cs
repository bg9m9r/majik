using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

public class SelfFightTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Theory]
    [InlineData("Target creature deals damage to itself equal to its power.")]
    // Trailing rider (Cut Propulsion) — captured by the regex's allowance,
    // dropped at resolution; bind still succeeds.
    [InlineData("Target creature deals damage to itself equal to its power. If that creature has flying, it deals twice that much damage to itself instead.")]
    public void Matches_TargetCreatureSelfFight(string oracle)
    {
        new SelfFightTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Each-creature variant is a different effect tree — separate template owns it.
    [InlineData("Each creature deals damage to itself equal to its power.")]
    // Two-target fight (CR 701.13) — FightTemplate owns this.
    [InlineData("Target creature you control fights target creature you don't control.")]
    // Direct damage, not self-fight.
    [InlineData("Lightning Bolt deals 3 damage to target creature.")]
    public void DoesNotMatch_OutOfFamily(string oracle)
    {
        new SelfFightTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }
}
