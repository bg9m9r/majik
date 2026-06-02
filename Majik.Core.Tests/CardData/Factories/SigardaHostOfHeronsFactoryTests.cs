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
/// Unit tests for <see cref="SigardaHostOfHeronsFactory"/> (Avacyn
/// Restored).
///
/// Covers:
/// - Identity ({2}{G}{W} Legendary Creature — Angel 5/5).
/// - Flying + Hexproof keyword markers (CR 702.9, CR 702.11).
/// - <see cref="NamedCardFactory"/> dispatch.
///
/// The printed "Spells and abilities your opponents control can't cause
/// you to sacrifice permanents." rider is intentionally NOT covered —
/// see factory class summary for the deferred-primitive note.
/// </summary>
[Trait("Color", "M")]
public class SigardaHostOfHeronsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Sigarda_Identity_LegendaryAngel55()
    {
        var c = SigardaHostOfHeronsFactory.Create(_alice);

        c.Name.Should().Be("Sigarda, Host of Herons");
        c.ManaCost.Should().Be("{2}{G}{W}");
        c.Power.Should().Be(5);
        c.Toughness.Should().Be(5);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sigarda_HasFlyingAndHexproofKeywords()
    {
        var c = SigardaHostOfHeronsFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Should().Contain(k => k.Keyword == "Flying");
        keywords.Should().Contain(k => k.Keyword == "Hexproof");
    }
    [Fact]
    public void Sigarda_NullOwner_Throws()
    {
        var act = () => SigardaHostOfHeronsFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
