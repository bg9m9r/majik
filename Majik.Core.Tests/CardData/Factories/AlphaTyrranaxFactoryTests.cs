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
/// Unit tests for <see cref="AlphaTyrranaxFactory"/>.
///
/// Card: Alpha Tyrranax — Creature — Dinosaur Beast {4}{G}{G} 6/5 (Scars of Mirrodin).
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
public class AlphaTyrranaxFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AlphaTyrranax_Identity()
    {
        var c = AlphaTyrranaxFactory.Create(_alice);

        c.Name.Should().Be("Alpha Tyrranax");
        c.ManaCost.Should().Be("{4}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AlphaTyrranax_ManaValue_IsSix()
    {
        var c = AlphaTyrranaxFactory.Create(_alice);

        c.ManaCost.Should().Be("{4}{G}{G}",
            "mana value 6: four generic plus two Green pips");
    }

    [Fact]
    public void AlphaTyrranax_Colors_ContainsGreenOnly()
    {
        var c = AlphaTyrranaxFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Alpha Tyrranax costs {4}{G}{G}");
        colors.Should().HaveCount(1, "Alpha Tyrranax is exactly Green");
    }

    [Fact]
    public void AlphaTyrranax_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Alpha Tyrranax", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Alpha Tyrranax");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
    }

    [Fact]
    public void AlphaTyrranax_IsVanilla_NoAbilities()
    {
        var c = AlphaTyrranaxFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Alpha Tyrranax is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Alpha Tyrranax has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Alpha Tyrranax has no activated abilities");
    }
}
