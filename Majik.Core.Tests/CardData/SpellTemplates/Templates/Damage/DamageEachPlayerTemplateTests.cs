using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

public class DamageEachPlayerTemplateTests
{
    private static SpellBindContext Ctx(string name, string oracle)
    {
        var e = new CardEntity { Name = name, OracleText = oracle, TypeLine = "Sorcery" };
        return new SpellBindContext(e, new Player("X", 20), x => x, null, null);
    }

    [Theory]
    [InlineData("Flame Rift", "Flame Rift deals 4 damage to each player.")]
    [InlineData("Mana Clash", "Mana Clash deals 1 damage to each player whose coin comes up tails.")]
    public void Binds_DealsNDamageToEachPlayer(string name, string oracle)
    {
        new DamageEachPlayerTemplate().TryBind(Ctx(name, oracle)).Should().NotBeNull();
    }

    [Fact]
    public void RejectsEachOpponentVariant()
    {
        // "each opponent" → handled by DealsNDamageEachOpponentTemplate.
        new DamageEachPlayerTemplate().TryBind(
            Ctx("Boltwave", "Boltwave deals 2 damage to each opponent."))
            .Should().BeNull();
    }
}
