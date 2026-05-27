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
/// Unit tests for <see cref="VastwoodGorgerFactory"/>.
///
/// Card: Vastwood Gorger — Creature — Wurm {5}{G} 5/6 (Magic 2012 /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class VastwoodGorgerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void VastwoodGorger_Identity()
    {
        var c = VastwoodGorgerFactory.Create(_alice);

        c.Name.Should().Be("Vastwood Gorger");
        c.ManaCost.Should().Be("{5}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
        c.Power.Should().Be(5);
        c.Toughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VastwoodGorger_ManaValue_IsSix()
    {
        var c = VastwoodGorgerFactory.Create(_alice);

        c.ManaCost.Should().Be("{5}{G}",
            "mana value 6: five generic plus one Green pip");
    }

    [Fact]
    public void VastwoodGorger_Colors_ContainsGreenOnly()
    {
        var c = VastwoodGorgerFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Vastwood Gorger costs {5}{G}");
        colors.Should().HaveCount(1, "Vastwood Gorger is exactly Green");
    }

    [Fact]
    public void VastwoodGorger_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Vastwood Gorger", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Vastwood Gorger");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
    }

    [Fact]
    public void VastwoodGorger_IsVanilla_NoAbilities()
    {
        var c = VastwoodGorgerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Vastwood Gorger is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Vastwood Gorger has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Vastwood Gorger has no activated abilities");
    }
}
