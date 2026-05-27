using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards.Types;
using Xunit;

namespace Majik.Core.Tests.Cards.Types;

/// <summary>
/// Verifies that Drake and Werewolf are present in the CardSubtype enum and
/// that TypeLineParser can parse them from Scryfall-style type-line strings.
/// </summary>
public class CardSubtypeAdditionsTests
{
    [Theory]
    [InlineData("Drake")]
    [InlineData("Werewolf")]
    public void CardSubtype_ContainsNewSubtype(string subtypeName)
    {
        Enum.IsDefined(typeof(CardSubtype), subtypeName)
            .Should().BeTrue($"CardSubtype.{subtypeName} must exist");
    }

    [Theory]
    [InlineData("Creature — Drake", CardSubtype.Drake)]
    [InlineData("Creature — Werewolf", CardSubtype.Werewolf)]
    public void TypeLineParser_ParsesNewSubtypes(string typeLine, CardSubtype expected)
    {
        var parsed = TypeLineParser.Parse(typeLine);
        parsed.Subtypes.Should().Contain(expected);
    }
}
