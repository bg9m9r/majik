using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CrawWurmFactory"/>.
///
/// Card: Craw Wurm — Creature — Wurm {4}{G}{G} 6/4 (Alpha / Modern reprints).
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
[Trait("Color", "G")]
public class CrawWurmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CrawWurm_Identity()
    {
        var c = CrawWurmFactory.Create(_alice);

        c.Name.Should().Be("Craw Wurm");
        c.ManaCost.Should().Be("{4}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CrawWurm_ManaValue_IsSix()
    {
        var c = CrawWurmFactory.Create(_alice);

        c.ManaCost.Should().Be("{4}{G}{G}",
            "mana value 6: four generic plus two Green pips");
    }

    [Fact]
    public void CrawWurm_Colors_ContainsGreenOnly()
    {
        var c = CrawWurmFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Craw Wurm costs {4}{G}{G}");
        colors.Should().HaveCount(1, "Craw Wurm is exactly Green");
    }
    [Fact]
    public void CrawWurm_IsVanilla_NoAbilities()
    {
        var c = CrawWurmFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Craw Wurm is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Craw Wurm has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Craw Wurm has no activated abilities");
    }
}
