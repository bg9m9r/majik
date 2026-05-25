using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Destroy;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Destroy;

public class DestroyTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void DestroyCreatureCmcLimitTemplate_MatchesCmcLimitOracle()
    {
        new DestroyCreatureCmcLimitTemplate()
            .TryBind(Ctx("Destroy target creature if its mana value is 2 or less."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DestroyUpToArtifactEnchantmentTemplate_MatchesUpToOracle()
    {
        new DestroyUpToArtifactEnchantmentTemplate()
            .TryBind(Ctx("Destroy up to two target artifacts and/or enchantments."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DestroyNonlandPermanentTemplate_MatchesNonlandPermanentOracle()
    {
        new DestroyNonlandPermanentTemplate()
            .TryBind(Ctx("Destroy target nonland permanent."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DestroyArtifactEnchantmentTemplate_MatchesArtifactOrEnchantmentOracle()
    {
        new DestroyArtifactEnchantmentTemplate()
            .TryBind(Ctx("Destroy target artifact or enchantment."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DestroyCreatureTemplate_MatchesDestroyCreatureOracle()
    {
        new DestroyCreatureTemplate()
            .TryBind(Ctx("Destroy target creature."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DestroyLandTemplate_MatchesDestroyLandOracle()
    {
        new DestroyLandTemplate()
            .TryBind(Ctx("Destroy target land."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DestroyPermanentTemplate_MatchesDestroyPermanentOracle()
    {
        new DestroyPermanentTemplate()
            .TryBind(Ctx("Destroy target permanent."))
            .Should().NotBeNull();
    }

    [Fact]
    public void DestroyPermanentTemplate_PriorityLessThanDestroyNonlandPermanentTemplate()
    {
        new DestroyPermanentTemplate().Priority
            .Should().BeLessThan(new DestroyNonlandPermanentTemplate().Priority);
    }
}
