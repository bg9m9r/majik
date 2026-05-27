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
/// Unit tests for <see cref="YavimayaWurmFactory"/>.
///
/// Card: Yavimaya Wurm — Creature — Wurm {4}{G}{G} 6/4 with Trample.
/// Oracle text: "Trample"
/// </summary>
public class YavimayaWurmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void YavimayaWurm_Identity()
    {
        var c = YavimayaWurmFactory.Create(_alice);

        c.Name.Should().Be("Yavimaya Wurm");
        c.ManaCost.Should().Be("{4}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void YavimayaWurm_ManaValue_IsSix()
    {
        var c = YavimayaWurmFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(6,
            "mana value 6: four generic + two Green pips");
    }

    [Fact]
    public void YavimayaWurm_Colors_ContainsGreenOnly()
    {
        var c = YavimayaWurmFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Yavimaya Wurm costs {4}{G}{G}");
        colors.Should().HaveCount(1, "Yavimaya Wurm is exactly Green");
    }

    [Fact]
    public void YavimayaWurm_HasTrampleKeyword()
    {
        var c = YavimayaWurmFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "Yavimaya Wurm has printed Trample (CR 702.19)");
    }

    [Fact]
    public void YavimayaWurm_NoOtherAbilities()
    {
        var c = YavimayaWurmFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Yavimaya Wurm has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Yavimaya Wurm has no activated abilities");
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Only Trample — no other keyword abilities");
    }

    [Fact]
    public void YavimayaWurm_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Yavimaya Wurm", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Yavimaya Wurm");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();

        var creature = (Creature)c;
        creature.Power.Should().Be(6);
        creature.Toughness.Should().Be(4);
    }
}
