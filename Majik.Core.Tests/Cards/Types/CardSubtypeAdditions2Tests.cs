using FluentAssertions;
using Majik.Core.Cards.Types;
using Xunit;

namespace Majik.Core.Tests.Cards.Types;

/// <summary>
/// Verifies that the eight creature subtypes added in the infra-creature-subtypes-2
/// batch are present in the <see cref="CardSubtype"/> enum so factory code and
/// TypeLineParser can reference them by name.
/// </summary>
public class CardSubtypeAdditions2Tests
{
    [Theory]
    [InlineData(CardSubtype.Rat)]
    [InlineData(CardSubtype.Elephant)]
    [InlineData(CardSubtype.Centaur)]
    [InlineData(CardSubtype.Griffin)]
    [InlineData(CardSubtype.Djinn)]
    [InlineData(CardSubtype.Ox)]
    [InlineData(CardSubtype.Spider)]
    [InlineData(CardSubtype.Ogre)]
    public void NewSubtype_IsDefinedInEnum(CardSubtype subtype)
    {
        Enum.IsDefined(typeof(CardSubtype), subtype).Should().BeTrue(
            because: $"{subtype} must be a recognised CardSubtype value");
    }
}
