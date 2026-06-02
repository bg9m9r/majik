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
/// Tests for <see cref="PlagueStingerFactory"/> — Creature — Phyrexian Insect
/// {1}{B} 1/1 with Flying (CR 702.9) + Infect (CR 702.90).
/// </summary>
[Trait("Color", "B")]
public class PlagueStingerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PlagueStinger_Identity()
    {
        var c = PlagueStingerFactory.Create(_alice);

        c.Name.Should().Be("Plague Stinger");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PlagueStinger_HasFlyingAndInfect()
    {
        var c = PlagueStingerFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().Contain("Flying",
            "Flying (CR 702.9) marker drives blocking-legality in combat.");
        keywords.Should().Contain("Infect",
            "Infect (CR 702.90) marker is attached so combat damage routes " +
            "to -1/-1 counters / poison counters once that primitive lands.");
    }
}
