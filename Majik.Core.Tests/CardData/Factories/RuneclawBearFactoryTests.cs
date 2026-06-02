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
/// Unit tests for <see cref="RuneclawBearFactory"/>.
///
/// Card: Runeclaw Bear — Creature — Bear {1}{G} 2/2.
/// Oracle text (verified against Scryfall): empty — vanilla. No printed
/// keywords, triggers, statics, or activated abilities.
/// </summary>
[Trait("Color", "G")]
public class RuneclawBearFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RuneclawBear_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Runeclaw Bear", _alice);

        c.Name.Should().Be("Runeclaw Bear");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bear).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}");
        // {1}{G} = 1 generic + 1 green = CMC 2 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(2);
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Runeclaw Bear has {G} in its mana cost");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void RuneclawBear_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Runeclaw Bear", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Runeclaw Bear is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Runeclaw Bear has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Runeclaw Bear has no activated abilities");
    }
}
