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
/// Unit tests for <see cref="HillGiantFactory"/>.
///
/// Card: Hill Giant — Creature — Giant {3}{R} 3/3.
/// French-vanilla — no printed keywords, triggers, statics, or activated
/// abilities. Identity is built from the embedded <c>hill-giant.json</c>
/// definition.
/// </summary>
[Trait("Color", "R")]
public class HillGiantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HillGiant_Identity()
    {
        var c = HillGiantFactory.Create(_alice);

        c.Name.Should().Be("Hill Giant");
        c.ManaCost.Should().Be("{3}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HillGiant_IsRed_ManaValueFour()
    {
        var c = HillGiantFactory.Create(_alice);

        // {3}{R} = 3 generic + 1 red pip = mana value 4 (CR 202.3); the
        // {R} pip makes the card red (CR 105.1 / CR 202.2).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Hill Giant has {R} in its mana cost");
    }

    [Fact]
    public void HillGiant_IsVanilla_NoAbilities()
    {
        var c = HillGiantFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Hill Giant is French-vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Hill Giant has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Hill Giant has no activated abilities");
        c.Abilities.OfType<StaticAbility>().Should().BeEmpty(
            "Hill Giant has no static abilities");
    }

    [Fact]
    public void HillGiant_DispatchesThroughNamedFactory()
    {
        // The [CardName("Hill Giant")] factory supersedes the fileless-JSON
        // and inline-fallback arms: NamedCardFactory must route to it.
        var c = (Creature)NamedCardFactory.Create("Hill Giant", _alice);

        c.Name.Should().Be("Hill Giant");
        c.ManaCost.Should().Be("{3}{R}");
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
    }
}
