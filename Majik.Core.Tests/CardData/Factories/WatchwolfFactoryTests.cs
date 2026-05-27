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
/// Unit tests for <see cref="WatchwolfFactory"/>.
///
/// Card: Watchwolf — Creature — Wolf {G}{W} 3/3 (Ravnica: City of Guilds).
/// Vanilla — no printed keywords, triggers, statics, or activated abilities.
/// </summary>
public class WatchwolfFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Watchwolf_Identity()
    {
        var c = WatchwolfFactory.Create(_alice);

        c.Name.Should().Be("Watchwolf");
        c.ManaCost.Should().Be("{G}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wolf).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Watchwolf_ManaValue_IsTwo()
    {
        var c = WatchwolfFactory.Create(_alice);

        c.ManaCost.Should().Be("{G}{W}",
            "mana value 2: one Green pip + one White pip");
    }

    [Fact]
    public void Watchwolf_Colors_ContainsGreenAndWhite()
    {
        var c = WatchwolfFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Watchwolf costs {G}");
        colors.Should().Contain(ManaColor.White, "Watchwolf costs {W}");
        colors.Should().HaveCount(2, "Watchwolf is exactly Green and White");
    }

    [Fact]
    public void Watchwolf_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Watchwolf", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Watchwolf");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wolf).Should().BeTrue();
    }

    [Fact]
    public void Watchwolf_IsVanilla_NoAbilities()
    {
        var c = WatchwolfFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Watchwolf is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Watchwolf has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Watchwolf has no activated abilities");
    }
}
