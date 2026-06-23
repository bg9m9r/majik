using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="YokedOxFactory"/>.
///
/// Card: Yoked Ox — Creature — Ox {W} 0/4.
/// Vanilla — empty oracle text (verified against Scryfall 2026-06); no printed
/// keywords, triggers, statics, or activated abilities. A cheap white wall.
/// </summary>
[Trait("Color", "W")]
public class YokedOxFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void YokedOx_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Yoked Ox", _alice);

        c.Name.Should().Be("Yoked Ox");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ox).Should().BeTrue();
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void YokedOx_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Yoked Ox", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Yoked Ox is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Yoked Ox has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Yoked Ox has no activated abilities");
    }
}
