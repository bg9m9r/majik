using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HornedTurtleFactory"/>.
///
/// Card: Horned Turtle — Creature — Turtle {2}{U} 1/4.
/// Vanilla — empty oracle text (verified against the embedded Modern seed);
/// no printed keywords, triggers, statics, or activated abilities. A blue
/// defensive wall with a non-standard P/T, so we pin exact identity.
/// </summary>
[Trait("Color", "U")]
public class HornedTurtleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HornedTurtle_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Horned Turtle", _alice);

        c.Name.Should().Be("Horned Turtle");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Turtle).Should().BeTrue();
        // {2}{U} = 2 generic + 1 blue = mana value 3 (CR 202.3).
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(4);
        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Horned Turtle has {U} in its mana cost");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
}
