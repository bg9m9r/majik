using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards.Types;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class TypeLineParserTests
{
    [Fact]
    public void Creature_Bear()
    {
        var p = TypeLineParser.Parse("Creature — Beast");

        p.Types.Should().Equal(CardType.Creature);
        p.Supertypes.Should().BeEmpty();
        p.Subtypes.Should().Contain(CardSubtype.Beast);
    }

    [Fact]
    public void BasicLand_Mountain()
    {
        var p = TypeLineParser.Parse("Basic Land — Mountain");

        p.Types.Should().Equal(CardType.Land);
        p.Supertypes.Should().Contain(CardSupertype.Basic);
        p.Subtypes.Should().Contain(CardSubtype.Mountain);
    }

    [Fact]
    public void LegendaryCreature_HumanWizard()
    {
        var p = TypeLineParser.Parse("Legendary Creature — Human Wizard");

        p.Types.Should().Equal(CardType.Creature);
        p.Supertypes.Should().Contain(CardSupertype.Legendary);
        p.Subtypes.Should().Contain(CardSubtype.Wizard);
    }

    [Fact]
    public void Instant_NoSubtypes()
    {
        var p = TypeLineParser.Parse("Instant");

        p.Types.Should().Equal(CardType.Instant);
        p.Subtypes.Should().BeEmpty();
    }

    [Fact]
    public void ArtifactCreature_DualType()
    {
        var p = TypeLineParser.Parse("Artifact Creature — Construct");

        p.Types.Should().Contain(CardType.Artifact);
        p.Types.Should().Contain(CardType.Creature);
    }

    [Fact]
    public void UnknownSubtype_Skipped()
    {
        var p = TypeLineParser.Parse("Creature — Gobbledygook");

        p.Types.Should().Equal(CardType.Creature);
        p.Subtypes.Should().BeEmpty();
    }

    [Fact]
    public void EmDash_AndAsciiDash_BothAccepted()
    {
        var withEmDash = TypeLineParser.Parse("Creature — Beast");
        var withAscii = TypeLineParser.Parse("Creature - Beast");

        withAscii.Subtypes.Should().Contain(CardSubtype.Beast);
        withEmDash.Subtypes.Should().Contain(CardSubtype.Beast);
    }
}
