using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CystbearerFactory"/> — Creature — Phyrexian Beast
/// {2}{G} 2/3 with Infect (CR 702.90).
/// </summary>
[Trait("Color", "G")]
public class CystbearerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Cystbearer_Identity()
    {
        var c = CystbearerFactory.Create(_alice);

        c.Name.Should().Be("Cystbearer");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Cystbearer_HasInfectKeyword()
    {
        var c = CystbearerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Infect").Should().BeTrue(
                "Infect (CR 702.90) marker is attached so the damage pipeline " +
                "can route -1/-1 counters / poison counters once that primitive lands.");
    }
}
