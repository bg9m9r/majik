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
/// Unit tests for <see cref="CanyonMinotaurFactory"/>.
///
/// Card: Canyon Minotaur — Creature — Minotaur Warrior {3}{R} 3/3.
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
public class CanyonMinotaurFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CanyonMinotaur_Identity()
    {
        var c = CanyonMinotaurFactory.Create(_alice);

        c.Name.Should().Be("Canyon Minotaur");
        c.ManaCost.Should().Be("{3}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Minotaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CanyonMinotaur_ManaValue_IsFour()
    {
        var c = CanyonMinotaurFactory.Create(_alice);

        // {3}{R} = 3 generic + 1 red = CMC 4 (CR 202.3).
        c.ManaCost.Should().Be("{3}{R}");
        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Canyon Minotaur has {R} in its mana cost");
    }

    [Fact]
    public void CanyonMinotaur_IsVanilla_NoAbilities()
    {
        var c = CanyonMinotaurFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Canyon Minotaur is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Canyon Minotaur has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Canyon Minotaur has no activated abilities");
    }

    [Fact]
    public void CanyonMinotaur_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Canyon Minotaur", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Canyon Minotaur");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Minotaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }
}
