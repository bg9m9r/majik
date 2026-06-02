using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GlistenerElfFactory"/> — Creature — Phyrexian Elf
/// Warrior {G} 1/1 with Infect (CR 702.90).
/// </summary>
[Trait("Color", "G")]
public class GlistenerElfFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GlistenerElf_Identity()
    {
        var c = GlistenerElfFactory.Create(_alice);

        c.Name.Should().Be("Glistener Elf");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GlistenerElf_HasInfectKeyword()
    {
        var c = GlistenerElfFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Infect").Should().BeTrue(
                "Infect (CR 702.90) marker is attached so the damage pipeline " +
                "can route -1/-1 counters / poison counters once that primitive lands.");
    }
}
