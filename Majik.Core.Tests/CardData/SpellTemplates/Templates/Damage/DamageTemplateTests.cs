using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

public class DamageTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void DamageAnyTargetTemplate_MatchesDealsDamageAnyTarget()
    {
        new DamageAnyTargetTemplate().TryBind(Ctx("Lightning Bolt deals 3 damage to any target."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DamagePlayerTemplate_MatchesDealsDamageToTargetPlayer()
    {
        new DamagePlayerTemplate().TryBind(Ctx("Lava Axe deals 5 damage to target player."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DealsDamageEachCreatureTemplate_MatchesDamageToEachCreature()
    {
        new DealsDamageEachCreatureTemplate().TryBind(Ctx("Pyroclasm deals 2 damage to each creature."))
            .Should().NotBeNull();
    }

    [Fact]
    public void EachOpponentLosesLifeTemplate_MatchesEachOpponentLosesLife()
    {
        new EachOpponentLosesLifeTemplate().TryBind(Ctx("Each opponent loses 2 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DealsXDamageAnyTemplate_MatchesXDamageToAnyTarget()
    {
        new DealsXDamageAnyTemplate().TryBind(Ctx("Fireball deals X damage to any target."))
            .Should().NotBeNull();
    }
}
