using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HillGiantFactory"/>.
///
/// Card: Hill Giant — Creature — Giant {3}{R} 3/3.
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
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
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HillGiant_ManaValue_IsFour()
    {
        var c = HillGiantFactory.Create(_alice);

        // {3}{R} = 3 generic + 1 red = CMC 4 (CR 202.3).
        c.ManaCost.Should().Be("{3}{R}");
        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Hill Giant has {R} in its mana cost");
    }

    [Fact]
    public void HillGiant_IsVanilla_NoAbilities()
    {
        var c = HillGiantFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Hill Giant is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Hill Giant has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Hill Giant has no activated abilities");
    }

    [Fact]
    public void HillGiant_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Hill Giant", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Hill Giant");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
    }
}
