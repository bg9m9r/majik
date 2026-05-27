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
/// Unit tests for <see cref="AlpineGrizzlyFactory"/>.
///
/// Card: Alpine Grizzly — Creature — Bear {2}{G} 4/2 (Khans of Tarkir).
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
public class AlpineGrizzlyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AlpineGrizzly_Identity()
    {
        var c = AlpineGrizzlyFactory.Create(_alice);

        c.Name.Should().Be("Alpine Grizzly");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bear).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AlpineGrizzly_ManaValue_IsThree()
    {
        var c = AlpineGrizzlyFactory.Create(_alice);

        c.ManaCost.Should().Be("{2}{G}",
            "mana value 3: two generic pips plus one Green pip");
    }

    [Fact]
    public void AlpineGrizzly_Colors_ContainsGreenOnly()
    {
        var c = AlpineGrizzlyFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Alpine Grizzly costs {2}{G}");
        colors.Should().HaveCount(1, "Alpine Grizzly is exactly Green");
    }

    [Fact]
    public void AlpineGrizzly_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Alpine Grizzly", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Alpine Grizzly");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bear).Should().BeTrue();
    }

    [Fact]
    public void AlpineGrizzly_IsVanilla_NoAbilities()
    {
        var c = AlpineGrizzlyFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Alpine Grizzly is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Alpine Grizzly has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Alpine Grizzly has no activated abilities");
    }
}
