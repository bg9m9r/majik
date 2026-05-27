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
/// Unit tests for <see cref="DeadlyRecluseFactory"/>.
///
/// Card: Deadly Recluse — {1}{G} Creature — Spider 1/2.
///   "Reach, Deathtouch" (CR 702.17, CR 702.2)
/// </summary>
public class DeadlyRecluseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DeadlyRecluse_Identity()
    {
        var c = DeadlyRecluseFactory.Create(_alice);

        c.Name.Should().Be("Deadly Recluse");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DeadlyRecluse_IsGreen()
    {
        var c = DeadlyRecluseFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Deadly Recluse has a {G} pip in its mana cost");
    }

    [Fact]
    public void DeadlyRecluse_ManaValueIsTwo()
    {
        var c = DeadlyRecluseFactory.Create(_alice);

        // {1}{G} → generic 1 + one coloured pip = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void DeadlyRecluse_HasReachKeywordMarker()
    {
        var c = DeadlyRecluseFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Reach").Should().BeTrue(
                "Deadly Recluse has Reach as a KeywordAbility marker (CR 702.17)");
    }

    [Fact]
    public void DeadlyRecluse_HasDeathtouchKeywordMarker()
    {
        var c = DeadlyRecluseFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Deathtouch").Should().BeTrue(
                "Deadly Recluse has Deathtouch as a KeywordAbility marker (CR 702.2)");
    }

    [Fact]
    public void DeadlyRecluse_HasExactlyTwoKeywords()
    {
        var c = DeadlyRecluseFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Reach and Deathtouch are the only printed keywords");
    }

    [Fact]
    public void DeadlyRecluse_NoTriggeredOrActivatedAbilities()
    {
        var c = DeadlyRecluseFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void DeadlyRecluse_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Deadly Recluse", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Deadly Recluse");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
    }
}
