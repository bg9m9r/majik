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
/// Unit tests for <see cref="WalkingCorpseFactory"/>.
///
/// Card: Walking Corpse — Creature — Zombie {1}{B} 2/2 (M12 / M13 / DDQ /
/// Modern reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class WalkingCorpseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WalkingCorpse_Identity()
    {
        var c = WalkingCorpseFactory.Create(_alice);

        c.Name.Should().Be("Walking Corpse");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WalkingCorpse_ManaValue_IsTwo()
    {
        var c = WalkingCorpseFactory.Create(_alice);

        c.ManaCost.Should().Be("{1}{B}",
            "mana value 2: one generic pip plus one Black pip");
    }

    [Fact]
    public void WalkingCorpse_Colors_ContainsBlackOnly()
    {
        var c = WalkingCorpseFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Black, "Walking Corpse costs {1}{B}");
        colors.Should().HaveCount(1, "Walking Corpse is exactly Black");
    }

    [Fact]
    public void WalkingCorpse_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Walking Corpse", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Walking Corpse");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
    }

    [Fact]
    public void WalkingCorpse_IsVanilla_NoAbilities()
    {
        var c = WalkingCorpseFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Walking Corpse is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Walking Corpse has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Walking Corpse has no activated abilities");
    }
}
