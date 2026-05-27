using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CharityExtractorFactory"/>.
///
/// Card: Charity Extractor — {3}{B} Creature — Human Knight 1/5.
///   "Lifelink"
/// </summary>
public class CharityExtractorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CharityExtractor_Identity()
    {
        var c = CharityExtractorFactory.Create(_alice);

        c.Name.Should().Be("Charity Extractor");
        c.ManaCost.Should().Be("{3}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CharityExtractor_IsBlack()
    {
        var c = CharityExtractorFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Black,
            "Charity Extractor has a {B} pip in its mana cost");
    }

    [Fact]
    public void CharityExtractor_ManaValueIsFour()
    {
        var c = CharityExtractorFactory.Create(_alice);

        // {3}{B} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(4);
    }

    [Fact]
    public void CharityExtractor_HasLifelinkKeywordMarker()
    {
        var c = CharityExtractorFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Charity Extractor ships with Lifelink as a KeywordAbility marker (CR 702.15)");
    }

    [Fact]
    public void CharityExtractor_NoOtherAbilities()
    {
        var c = CharityExtractorFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Lifelink is the only printed keyword");
    }

    [Fact]
    public void CharityExtractor_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Charity Extractor", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Charity Extractor");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }
}
