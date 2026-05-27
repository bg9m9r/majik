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
/// Unit tests for <see cref="MossViperFactory"/>.
///
/// Card: Moss Viper — {G} Creature — Snake 1/1.
///   "Deathtouch"
/// </summary>
public class MossViperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MossViper_Identity()
    {
        var c = MossViperFactory.Create(_alice);

        c.Name.Should().Be("Moss Viper");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MossViper_IsGreen()
    {
        var c = MossViperFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Moss Viper has one {G} pip in its mana cost");
    }

    [Fact]
    public void MossViper_ManaValueIsOne()
    {
        var c = MossViperFactory.Create(_alice);

        // {G} → one coloured pip = mana value 1 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(1);
    }

    [Fact]
    public void MossViper_HasDeathtouchKeywordMarker()
    {
        var c = MossViperFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Deathtouch").Should().BeTrue(
                "Moss Viper ships with Deathtouch as a KeywordAbility marker (CR 702.2)");
    }

    [Fact]
    public void MossViper_NoOtherAbilities()
    {
        var c = MossViperFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Deathtouch is the only printed keyword");
    }

    [Fact]
    public void MossViper_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Moss Viper", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Moss Viper");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
    }
}
