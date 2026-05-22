using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

public class AsymmetricFightTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Theory]
    [InlineData("Target creature you control deals damage equal to its power to target creature you don't control.")]
    [InlineData("Target creature you control deals damage equal to its power to target creature or planeswalker you don't control.")]
    [InlineData("Target creature you control deals damage equal to its power to target creature an opponent controls.")]
    [InlineData("Target creature you control deals damage equal to its power to target creature with flying.")]
    [InlineData("Target creature you control deals damage equal to its power to target player.")]
    public void Matches_AsymmetricFightFamily(string oracle)
    {
        new AsymmetricFightTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Trailing rider clauses dropped at resolution.
    [InlineData("Target creature you control deals damage equal to its power to target creature you don't control. Each opponent gets a poison counter.")]
    [InlineData("Target creature you control deals damage equal to its power to target creature you don't control. If the creature you control has trample, excess damage is dealt to that creature's controller instead.")]
    public void Matches_WithTrailingRiders(string oracle)
    {
        new AsymmetricFightTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // CR 701.13 bilateral fight — FightTemplate owns this.
    [InlineData("Target creature you control fights target creature you don't control.")]
    // Self-fight — SelfFightTemplate owns this.
    [InlineData("Target creature deals damage to itself equal to its power.")]
    public void DoesNotMatch_OutOfFamily(string oracle)
    {
        new AsymmetricFightTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }

    [Theory]
    // Mutiny family — opponent's creature deals damage to another of their
    // creatures. Broadened to match.
    [InlineData("Target creature an opponent controls deals damage equal to its power to another target creature that player controls.")]
    public void Matches_MutinyFamily(string oracle)
    {
        new AsymmetricFightTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }
}
