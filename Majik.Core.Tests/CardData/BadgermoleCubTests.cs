using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BadgermoleCubFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, subtype Bear, power/toughness, owner/controller)
/// - Zero abilities attached in v1 (earthbend ETB + tap-for-mana trigger are deferred)
/// </summary>
public class BadgermoleCubTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BadgermoleCub_NameIsCorrect()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        b.Name.Should().Be("Badgermole Cub");
    }

    [Fact]
    public void BadgermoleCub_IsCreature()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        b.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void BadgermoleCub_HasBearSubtype()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        b.HasSubtype(CardSubtype.Bear).Should().BeTrue("Badgermole Cub is a Bear");
    }

    [Fact]
    public void BadgermoleCub_HasCorrectStats()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        b.BasePower.Should().Be(1);
        b.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void BadgermoleCub_OwnerAndControllerAreSet()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BadgermoleCub_HasZeroAbilities_V1Shell()
    {
        var b = BadgermoleCubFactory.Create(_alice);

        b.Abilities.Should().BeEmpty(
            "v1 is a shell; earthbend ETB and tap-for-mana trigger are deferred");
    }
}
