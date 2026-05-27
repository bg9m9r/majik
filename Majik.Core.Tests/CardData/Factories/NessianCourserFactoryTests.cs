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
/// Unit tests for <see cref="NessianCourserFactory"/>.
///
/// Card: Nessian Courser — Creature — Centaur Warrior {2}{G} 3/3 (Theros /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class NessianCourserFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NessianCourser_Identity()
    {
        var c = NessianCourserFactory.Create(_alice);

        c.Name.Should().Be("Nessian Courser");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Centaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NessianCourser_ManaValue_IsThree()
    {
        var c = NessianCourserFactory.Create(_alice);

        c.ManaCost.Should().Be("{2}{G}",
            "mana value 3: two generic plus one Green pip");
    }

    [Fact]
    public void NessianCourser_Colors_ContainsGreenOnly()
    {
        var c = NessianCourserFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Nessian Courser costs {2}{G}");
        colors.Should().HaveCount(1, "Nessian Courser is exactly Green");
    }

    [Fact]
    public void NessianCourser_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Nessian Courser", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Nessian Courser");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Centaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void NessianCourser_IsVanilla_NoAbilities()
    {
        var c = NessianCourserFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Nessian Courser is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Nessian Courser has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Nessian Courser has no activated abilities");
    }
}
