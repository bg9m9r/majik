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
/// Unit tests for <see cref="TrainedArmodonFactory"/>.
///
/// Card: Trained Armodon — Creature — Elephant {1}{G}{G} 3/3 (Odyssey /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class TrainedArmodonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TrainedArmodon_Identity()
    {
        var c = TrainedArmodonFactory.Create(_alice);

        c.Name.Should().Be("Trained Armodon");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elephant).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TrainedArmodon_ManaValue_IsThree()
    {
        var c = TrainedArmodonFactory.Create(_alice);

        c.ManaCost.Should().Be("{1}{G}{G}",
            "mana value 3: one generic plus two Green pips");
    }

    [Fact]
    public void TrainedArmodon_Colors_ContainsGreenOnly()
    {
        var c = TrainedArmodonFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Trained Armodon costs {1}{G}{G}");
        colors.Should().HaveCount(1, "Trained Armodon is exactly Green");
    }

    [Fact]
    public void TrainedArmodon_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Trained Armodon", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Trained Armodon");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elephant).Should().BeTrue();
    }

    [Fact]
    public void TrainedArmodon_IsVanilla_NoAbilities()
    {
        var c = TrainedArmodonFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Trained Armodon is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Trained Armodon has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Trained Armodon has no activated abilities");
    }
}
